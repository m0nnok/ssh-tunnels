using System.Collections.ObjectModel;
using System.Text.Json;
using SshTunneler.Models;
using SshTunneler.Services;

namespace SshTunneler.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _draft;
    private readonly AskPassServer _askPass;
    private readonly string _originalJson;
    private SshServer? _selectedServer;
    private TunnelResource? _selectedResource;
    private string _testResult = "";
    private bool _isTesting;

    public SettingsViewModel(AppConfig config, AskPassServer askPass)
    {
        _draft = config.Clone();
        _askPass = askPass;

        Servers = new ObservableCollection<SshServer>(_draft.Servers);
        Resources = new ObservableCollection<TunnelResource>(_draft.Resources.OrderBy(r => r.Order));

        AddServerCommand = new RelayCommand(AddServer);
        DeleteServerCommand = new RelayCommand(DeleteServer, () => SelectedServer is not null);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        AddResourceCommand = new RelayCommand(AddResource);
        DeleteResourceCommand = new RelayCommand(DeleteResource, () => SelectedResource is not null);
        MoveResourceUpCommand = new RelayCommand(() => MoveResource(-1));
        MoveResourceDownCommand = new RelayCommand(() => MoveResource(1));

        SelectedServer = Servers.FirstOrDefault();
        SelectedResource = Resources.FirstOrDefault();
        _originalJson = Snapshot();
    }

    public ObservableCollection<SshServer> Servers { get; }

    public ObservableCollection<TunnelResource> Resources { get; }

    public UiSettings Ui => _draft.Ui;

    public IReadOnlyList<AuthMethod> AuthMethods { get; } =
        [AuthMethod.Agent, AuthMethod.KeyFile, AuthMethod.Password];

    public RelayCommand AddServerCommand { get; }
    public RelayCommand DeleteServerCommand { get; }
    public AsyncRelayCommand TestConnectionCommand { get; }
    public RelayCommand AddResourceCommand { get; }
    public RelayCommand DeleteResourceCommand { get; }
    public RelayCommand MoveResourceUpCommand { get; }
    public RelayCommand MoveResourceDownCommand { get; }

    public SshServer? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (!SetField(ref _selectedServer, value)) return;
            TestResult = "";
            DeleteServerCommand.RaiseCanExecuteChanged();
        }
    }

    public TunnelResource? SelectedResource
    {
        get => _selectedResource;
        set
        {
            if (!SetField(ref _selectedResource, value)) return;
            DeleteResourceCommand.RaiseCanExecuteChanged();
        }
    }

    public string TestResult
    {
        get => _testResult;
        private set => SetField(ref _testResult, value);
    }

    public bool IsTesting
    {
        get => _isTesting;
        private set => SetField(ref _isTesting, value);
    }

    /// <summary>True when closing now would throw away edits.</summary>
    public bool HasChanges => Snapshot() != _originalJson;

    /// <summary>Collects the edits back into a configuration object for the main view model.</summary>
    public AppConfig BuildResult()
    {
        Snapshot();
        return _draft;
    }

    /// <summary>Folds the edited lists back into the draft and serialises it for change detection.</summary>
    private string Snapshot()
    {
        _draft.Servers = Servers.ToList();
        for (var i = 0; i < Resources.Count; i++) Resources[i].Order = i;
        _draft.Resources = Resources.ToList();
        return JsonSerializer.Serialize(_draft);
    }

    public string? Validate()
    {
        foreach (var server in Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Name)) return "У каждого сервера должно быть название";
            if (string.IsNullOrWhiteSpace(server.Host)) return $"У сервера {server.Name} не заполнен адрес";
        }

        foreach (var resource in Resources)
        {
            if (string.IsNullOrWhiteSpace(resource.Name)) return "У каждого ресурса должно быть название";
            if (Servers.All(s => s.Id != resource.ServerId)) return $"Для ресурса {resource.Name} не выбран сервер";
            if (resource.RemotePort is < 1 or > 65535) return $"У ресурса {resource.Name} некорректный удалённый порт";
            if (resource.LocalPort is < 0 or > 65535) return $"У ресурса {resource.Name} некорректный локальный порт";
        }

        if (Ui.LocalPortRangeStart is < 1 or > 65535 || Ui.LocalPortRangeEnd < Ui.LocalPortRangeStart)
            return "Некорректный диапазон локальных портов";

        return null;
    }

    private void AddServer()
    {
        var server = new SshServer { Name = "Новый сервер", Host = "", Port = 22 };
        Servers.Add(server);
        SelectedServer = server;
    }

    private void DeleteServer()
    {
        if (SelectedServer is null) return;

        var id = SelectedServer.Id;
        Servers.Remove(SelectedServer);
        foreach (var resource in Resources.Where(r => r.ServerId == id)) resource.ServerId = Guid.Empty;
        SelectedServer = Servers.FirstOrDefault();
    }

    private void AddResource()
    {
        var resource = new TunnelResource
        {
            Name = "Новый ресурс",
            ServerId = SelectedServer?.Id ?? Servers.FirstOrDefault()?.Id ?? Guid.Empty,
            RemotePort = 8080
        };

        Resources.Add(resource);
        SelectedResource = resource;
    }

    private void DeleteResource()
    {
        if (SelectedResource is null) return;
        Resources.Remove(SelectedResource);
        SelectedResource = Resources.FirstOrDefault();
    }

    private void MoveResource(int offset)
    {
        if (SelectedResource is null) return;

        var index = Resources.IndexOf(SelectedResource);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Resources.Count) return;

        Resources.Move(index, target);
        SelectedResource = Resources[target];
    }

    private async Task TestConnectionAsync()
    {
        if (SelectedServer is null) return;

        IsTesting = true;
        TestResult = "Проверяем...";
        try
        {
            var result = await ConnectionTester.TestAsync(SelectedServer, _askPass);
            TestResult = result.Success ? $"OK: {result.Message}" : $"Ошибка: {result.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }
}
