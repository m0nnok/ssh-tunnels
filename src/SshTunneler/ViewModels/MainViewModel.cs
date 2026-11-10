using System.Collections.ObjectModel;
using Avalonia.Threading;
using SshTunneler.Models;
using SshTunneler.Services;

namespace SshTunneler.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IConfigStore _store;
    private readonly Func<string, Task> _copyToClipboard;
    private readonly DispatcherTimer _logTimer;
    private ResourceItemViewModel? _logSource;
    private string _statusLine = "";
    private bool _isLogVisible;

    public MainViewModel(AppConfig config, IConfigStore store, TunnelManager tunnels, Func<string, Task> copyToClipboard)
    {
        Config = config;
        _store = store;
        Tunnels = tunnels;
        _copyToClipboard = copyToClipboard;

        Tunnels.StatusChanged += OnStatusChanged;

        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(null));
        ToggleLogCommand = new RelayCommand(() => IsLogVisible = !IsLogVisible);
        HideCommand = new RelayCommand(() => HideRequested?.Invoke());
        DisconnectAllCommand = new AsyncRelayCommand(() => Tunnels.DisconnectAllAsync());

        _logTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _logTimer.Tick += (_, _) => RefreshLog();

        Reload();
    }

    public event Action<Guid?>? SettingsRequested;
    public event Action? HideRequested;

    public AppConfig Config { get; private set; }

    public TunnelManager Tunnels { get; }

    public ObservableCollection<ResourceItemViewModel> Items { get; } = new();

    public ObservableCollection<string> LogLines { get; } = new();

    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand ToggleLogCommand { get; }
    public RelayCommand HideCommand { get; }
    public AsyncRelayCommand DisconnectAllCommand { get; }

    public UiSettings Ui => Config.Ui;

    public bool HasResources => Items.Count > 0;

    public string LogTitle => _logSource is null ? "Лог" : $"Лог — {_logSource.Name}";

    public string StatusLine
    {
        get => _statusLine;
        private set => SetField(ref _statusLine, value);
    }

    public bool IsLogVisible
    {
        get => _isLogVisible;
        set
        {
            if (!SetField(ref _isLogVisible, value)) return;
            if (value)
            {
                _logSource ??= Items.FirstOrDefault();
                RefreshLog();
                _logTimer.Start();
            }
            else
            {
                _logTimer.Stop();
            }
        }
    }

    /// <summary>Rebuilds the buttons from the current configuration, keeping live statuses.</summary>
    public void Reload()
    {
        Items.Clear();
        foreach (var resource in Config.Resources.OrderBy(r => r.Order).ThenBy(r => r.Name))
        {
            var serverName = Config.FindServer(resource.ServerId)?.ToString() ?? "нет сервера";
            var item = new ResourceItemViewModel(resource, serverName, this)
            {
                Status = Tunnels.GetStatus(resource.Id)
            };
            Items.Add(item);
        }

        OnPropertyChanged(nameof(HasResources));
        OnPropertyChanged(nameof(Ui));
    }

    public void ApplyConfig(AppConfig config)
    {
        Config = config;
        Tunnels.UpdateConfig(config);
        Save();
        Reload();
        AutoStartService.Sync(config.Ui.AutoStartWithWindows);
        _ = Tunnels.DisconnectRemovedAsync();
    }

    public void Save() => _store.Save(Config);

    public async Task ToggleAsync(ResourceItemViewModel item)
    {
        if (item.State is TunnelState.Connected)
        {
            await Tunnels.DisconnectAsync(item.Id);
            return;
        }

        if (item.State is TunnelState.Connecting or TunnelState.Reconnecting)
        {
            await Tunnels.DisconnectAsync(item.Id);
            return;
        }

        var connected = await Tunnels.ConnectAsync(item.Id);
        if (connected && item.Resource.AutoOpenBrowser) OpenInBrowser(item);
    }

    public async Task ReconnectAsync(ResourceItemViewModel item)
    {
        await Tunnels.DisconnectAsync(item.Id);
        await Tunnels.ConnectAsync(item.Id);
    }

    public Task DisconnectAsync(ResourceItemViewModel item) => Tunnels.DisconnectAsync(item.Id);

    public void OpenInBrowser(ResourceItemViewModel item)
    {
        var port = item.Status.LocalPort > 0 ? item.Status.LocalPort : item.Resource.LocalPort;
        if (port == 0)
        {
            StatusLine = "Туннель не поднят: нечего открывать";
            return;
        }

        try
        {
            BrowserLauncher.Open(BrowserLauncher.BuildUrl(item.Resource, port), Config.Ui.BrowserPath);
        }
        catch (Exception ex)
        {
            StatusLine = $"Не удалось открыть браузер: {ex.Message}";
        }
    }

    public async Task CopyUrlAsync(ResourceItemViewModel item)
    {
        var port = item.Status.LocalPort > 0 ? item.Status.LocalPort : item.Resource.LocalPort;
        try
        {
            await _copyToClipboard(BrowserLauncher.BuildUrl(item.Resource, port));
            StatusLine = "URL скопирован";
        }
        catch (Exception ex)
        {
            StatusLine = $"Буфер обмена недоступен: {ex.Message}";
        }
    }

    public void ShowLog(ResourceItemViewModel item)
    {
        _logSource = item;
        OnPropertyChanged(nameof(LogTitle));
        IsLogVisible = true;
        RefreshLog();
    }

    public void EditResource(ResourceItemViewModel item) => SettingsRequested?.Invoke(item.Id);

    /// <summary>Raises tunnels marked as auto-connect right after the window is up.</summary>
    public async Task ConnectAutoStartAsync()
    {
        foreach (var item in Items.Where(i => i.Resource.AutoConnectOnStart).ToArray())
            await Tunnels.ConnectAsync(item.Id);
    }

    private void RefreshLog()
    {
        if (!IsLogVisible) return;

        _logSource ??= Items.FirstOrDefault();
        if (_logSource is null) return;

        var lines = Tunnels.GetLog(_logSource.Id);
        if (lines.Count == LogLines.Count) return;

        LogLines.Clear();
        foreach (var line in lines) LogLines.Add(line);
    }

    private void OnStatusChanged(object? sender, TunnelStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var item = Items.FirstOrDefault(i => i.Id == status.ResourceId);
            if (item is null) return;

            item.Status = status;
            if (status.Message is not null)
                StatusLine = $"{item.Name}: {status.Message}";
            else if (status.State == TunnelState.Connected)
                StatusLine = $"{item.Name}: туннель на 127.0.0.1:{status.LocalPort}";
            else if (status.State == TunnelState.Idle)
                StatusLine = $"{item.Name}: отключён";

            if (ReferenceEquals(item, _logSource)) RefreshLog();
        });
    }
}
