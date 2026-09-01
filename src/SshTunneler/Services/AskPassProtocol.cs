namespace SshTunneler.Services;

/// <summary>
/// Contract between the running app and the askpass helper it spawns as a child of ssh.exe.
/// ssh.exe cannot read a password from stdin, but it does call SSH_ASKPASS, so the app starts
/// itself with --askpass and hands the secret over a private named pipe.
/// </summary>
public static class AskPassProtocol
{
    public const string Switch = "--askpass";
    public const string PipeEnvVar = "SSHTUNNELER_ASKPASS_PIPE";
    public const string TokenEnvVar = "SSHTUNNELER_ASKPASS_TOKEN";
    public const string LabelEnvVar = "SSHTUNNELER_ASKPASS_LABEL";

    public sealed record Request(string Token, string Prompt);

    public sealed record Response(bool Found, string? Secret);
}
