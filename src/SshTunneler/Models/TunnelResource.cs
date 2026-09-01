namespace SshTunneler.Models;

/// <summary>A resource behind a server: one local port forward plus how to open it.</summary>
public sealed class TunnelResource : ObservableObject
{
    private string _name = "";
    private string _description = "";
    private Guid _serverId;
    private string _remoteHost = "127.0.0.1";
    private int _remotePort = 80;
    private int _localPort;
    private string _urlTemplate = "http://127.0.0.1:{localPort}";
    private bool _autoOpenBrowser = true;
    private bool _autoConnectOnStart;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get => _name; set => SetField(ref _name, value); }

    public string Description { get => _description; set => SetField(ref _description, value); }

    public Guid ServerId { get => _serverId; set => SetField(ref _serverId, value); }

    /// <summary>Address the port is mapped to on the server side, usually 127.0.0.1.</summary>
    public string RemoteHost { get => _remoteHost; set => SetField(ref _remoteHost, value); }

    public int RemotePort { get => _remotePort; set => SetField(ref _remotePort, value); }

    /// <summary>Local port to bind. 0 means pick a free one from the configured range.</summary>
    public int LocalPort { get => _localPort; set => SetField(ref _localPort, value); }

    /// <summary>URL opened in the browser. Supports {localPort}, {remotePort}, {remoteHost}.</summary>
    public string UrlTemplate { get => _urlTemplate; set => SetField(ref _urlTemplate, value); }

    public bool AutoOpenBrowser { get => _autoOpenBrowser; set => SetField(ref _autoOpenBrowser, value); }

    public bool AutoConnectOnStart { get => _autoConnectOnStart; set => SetField(ref _autoConnectOnStart, value); }

    public int Order { get; set; }

    public override string ToString() => Name;
}
