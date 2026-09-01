namespace SshTunneler.Models;

public enum AuthMethod
{
    /// <summary>Rely on ssh-agent and defaults from the ssh config. No secret stored.</summary>
    Agent,
    /// <summary>Explicit private key file, optionally protected by a stored passphrase.</summary>
    KeyFile,
    /// <summary>Password authentication, password stored encrypted.</summary>
    Password
}

public enum TunnelState
{
    Idle,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}
