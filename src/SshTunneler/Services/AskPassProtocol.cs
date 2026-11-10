using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SshTunneler.Services;

/// <summary>
/// Contract between the running app and the askpass helper it spawns as a child of ssh.
/// ssh cannot read a password from stdin, but it does call SSH_ASKPASS, so the app starts
/// itself with --askpass and hands the secret over a private named pipe.
/// </summary>
public static class AskPassProtocol
{
    public const string Switch = "--askpass";
    public const string PipeEnvVar = "SSHTUNNELER_ASKPASS_PIPE";
    public const string TokenEnvVar = "SSHTUNNELER_ASKPASS_TOKEN";
    public const string LabelEnvVar = "SSHTUNNELER_ASKPASS_LABEL";

    /// <summary>Longest Unix socket path macOS accepts: sun_path is 104 bytes with the terminator.</summary>
    public const int MaxSocketPathBytes = 103;

    public sealed record Request(string Token, string Prompt);

    public sealed record Response(bool Found, string? Secret);

    /// <summary>
    /// Name for a fresh askpass pipe. On Unix the pipe is a domain socket, and the default spot
    /// (per-user TMPDIR + "CoreFxPipe_" + a GUID name) is one byte over the macOS limit, so a short
    /// absolute path is used instead — .NET takes a rooted pipe name as the socket path as is.
    /// </summary>
    public static string NewPipeName()
    {
        if (OperatingSystem.IsWindows()) return "SshTunneler." + Guid.NewGuid().ToString("N");

        var name = "sshtun-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var path = Path.Combine(Path.GetTempPath(), name);
        return Encoding.UTF8.GetByteCount(path) <= MaxSocketPathBytes ? path : Path.Combine("/tmp", name);
    }

    /// <summary>Environment for an ssh child process that should take its secrets from this app.</summary>
    public static Dictionary<string, string> BuildSshEnvironment(string pipeName, string token, string label)
    {
        var environment = new Dictionary<string, string>
        {
            // ssh cannot read secrets from stdin, so it asks this very executable instead.
            ["SSH_ASKPASS"] = Environment.ProcessPath ?? "",
            ["SSH_ASKPASS_REQUIRE"] = "force",
            [PipeEnvVar] = pipeName,
            [TokenEnvVar] = token,
            [LabelEnvVar] = label
        };

        // Windows 10 ships OpenSSH 8.1, older than SSH_ASKPASS_REQUIRE: there ssh only turns to
        // askpass when it has no terminal and DISPLAY is set.
        if (OperatingSystem.IsWindows()) environment["DISPLAY"] = "localhost:0";

        return environment;
    }
}
