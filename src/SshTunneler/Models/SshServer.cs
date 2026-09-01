namespace SshTunneler.Models;

/// <summary>An SSH access: everything needed to open a session to one host.</summary>
public sealed class SshServer : ObservableObject
{
    private string _name = "";
    private string _host = "";
    private int _port = 22;
    private string _user = "";
    private AuthMethod _authMethod = AuthMethod.Agent;
    private string? _keyPath;
    private string? _secret;
    private string? _extraOptions;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name, for example prod-web.</summary>
    public string Name { get => _name; set => SetField(ref _name, value); }

    /// <summary>Host name or IP. May also be a Host alias from the ssh config.</summary>
    public string Host { get => _host; set => SetField(ref _host, value); }

    public int Port { get => _port; set => SetField(ref _port, value); }

    /// <summary>Login. Empty means let ssh decide (config or current user).</summary>
    public string User { get => _user; set => SetField(ref _user, value); }

    public AuthMethod AuthMethod { get => _authMethod; set => SetField(ref _authMethod, value); }

    /// <summary>Path to the private key when AuthMethod is KeyFile.</summary>
    public string? KeyPath { get => _keyPath; set => SetField(ref _keyPath, value); }

    /// <summary>Key passphrase or account password. Lives only inside the encrypted config.</summary>
    public string? Secret { get => _secret; set => SetField(ref _secret, value); }

    /// <summary>Extra ssh options, space or newline separated, for example -o ProxyJump=bastion.</summary>
    public string? ExtraOptions { get => _extraOptions; set => SetField(ref _extraOptions, value); }

    public string DisplayTarget => string.IsNullOrWhiteSpace(User) ? Host : $"{User}@{Host}";

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? DisplayTarget : Name;
}
