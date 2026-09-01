using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using SshTunneler.Models;
using SshTunneler.Services;

namespace SshTunneler.ViewModels;

/// <summary>One button in the main window: a resource plus the live state of its tunnel.</summary>
public sealed class ResourceItemViewModel : ObservableObject
{
    private static readonly Brush IdleBrush = Freeze(Color.FromRgb(0x6B, 0x6B, 0x75));
    private static readonly Brush BusyBrush = Freeze(Color.FromRgb(0xE0, 0xA8, 0x30));
    private static readonly Brush OkBrush = Freeze(Color.FromRgb(0x3F, 0xC4, 0x6B));
    private static readonly Brush FailBrush = Freeze(Color.FromRgb(0xE0, 0x54, 0x54));

    private TunnelStatus _status;

    public ResourceItemViewModel(TunnelResource resource, string serverName, MainViewModel owner)
    {
        Resource = resource;
        ServerName = serverName;
        _status = TunnelStatus.Idle(resource.Id);

        ToggleCommand = new AsyncRelayCommand(() => owner.ToggleAsync(this));
        ReconnectCommand = new AsyncRelayCommand(() => owner.ReconnectAsync(this));
        DisconnectCommand = new AsyncRelayCommand(() => owner.DisconnectAsync(this));
        OpenBrowserCommand = new RelayCommand(() => owner.OpenInBrowser(this));
        CopyUrlCommand = new RelayCommand(() => owner.CopyUrl(this));
        ShowLogCommand = new RelayCommand(() => owner.ShowLog(this));
        EditCommand = new RelayCommand(() => owner.EditResource(this));
    }

    public TunnelResource Resource { get; }

    public string ServerName { get; }

    public Guid Id => Resource.Id;

    public string Name => string.IsNullOrWhiteSpace(Resource.Name) ? "(без названия)" : Resource.Name;

    public AsyncRelayCommand ToggleCommand { get; }
    public AsyncRelayCommand ReconnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public RelayCommand OpenBrowserCommand { get; }
    public RelayCommand CopyUrlCommand { get; }
    public RelayCommand ShowLogCommand { get; }
    public RelayCommand EditCommand { get; }

    public TunnelStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ToolTipText));
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public TunnelState State => _status.State;

    public bool IsBusy => _status.State is TunnelState.Connecting or TunnelState.Reconnecting;

    public Brush StatusBrush => _status.State switch
    {
        TunnelState.Connected => OkBrush,
        TunnelState.Connecting or TunnelState.Reconnecting => BusyBrush,
        TunnelState.Failed => FailBrush,
        _ => IdleBrush
    };

    public string StatusText => _status.State switch
    {
        TunnelState.Connected => $"127.0.0.1:{_status.LocalPort}",
        TunnelState.Connecting => "подключение...",
        TunnelState.Reconnecting => "переподключение",
        TunnelState.Failed => _status.Message ?? "ошибка",
        _ => $"{Resource.RemoteHost}:{Resource.RemotePort}"
    };

    public string ToolTipText
    {
        get
        {
            var lines = new List<string> { $"{Name} — {ServerName}" };
            if (!string.IsNullOrWhiteSpace(Resource.Description)) lines.Add(Resource.Description);
            lines.Add(_status.LocalPort > 0
                ? $"127.0.0.1:{_status.LocalPort} -> {Resource.RemoteHost}:{Resource.RemotePort}"
                : $"-> {Resource.RemoteHost}:{Resource.RemotePort}");
            if (_status.Message is not null) lines.Add(_status.Message);
            return string.Join(Environment.NewLine, lines);
        }
    }

    public void RefreshResourceInfo()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ToolTipText));
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
