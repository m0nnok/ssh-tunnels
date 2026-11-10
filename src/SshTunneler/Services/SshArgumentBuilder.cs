using System.IO;
using SshTunneler.Models;

namespace SshTunneler.Services;

/// <summary>Builds ssh command lines. Pure functions, covered by unit tests.</summary>
public static class SshArgumentBuilder
{
    /// <summary>
    /// Full path of the OpenSSH client that ships with the OS, then whatever ssh is on PATH,
    /// then the bare name as a last resort.
    /// </summary>
    public static string ResolveSshPath()
    {
        // Apps started from Finder or launchd get a bare PATH, so the system client comes first.
        var system = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe")
            : "/usr/bin/ssh";
        if (File.Exists(system)) return system;

        var fileName = OperatingSystem.IsWindows() ? "ssh.exe" : "ssh";
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry, ignore.
            }
        }

        return fileName;
    }

    /// <summary>Arguments for a background port forward: ssh -N -L local:remote target.</summary>
    public static List<string> BuildTunnelArgs(SshServer server, TunnelResource resource, int localPort)
    {
        var args = BuildCommonArgs(server);
        args.Add("-N");
        args.Add("-T");
        args.Add("-L");
        args.Add($"127.0.0.1:{localPort}:{resource.RemoteHost}:{resource.RemotePort}");
        args.Add(Target(server));
        return args;
    }

    /// <summary>Arguments for a connectivity check: authenticate, run true, disconnect.</summary>
    public static List<string> BuildTestArgs(SshServer server)
    {
        var args = BuildCommonArgs(server);
        args.Add("-o");
        args.Add("ConnectTimeout=10");
        args.Add(Target(server));
        args.Add("exit");
        return args;
    }

    private static List<string> BuildCommonArgs(SshServer server)
    {
        var args = new List<string>
        {
            "-v",
            "-o", "ExitOnForwardFailure=yes",
            "-o", "ServerAliveInterval=15",
            "-o", "ServerAliveCountMax=3",
            "-o", "StrictHostKeyChecking=accept-new",
            "-o", "NumberOfPasswordPrompts=1"
        };

        switch (server.AuthMethod)
        {
            case AuthMethod.Agent:
                // Never block on a prompt: without an agent key this must fail fast.
                args.Add("-o");
                args.Add("BatchMode=yes");
                break;

            case AuthMethod.KeyFile:
                if (!string.IsNullOrWhiteSpace(server.KeyPath))
                {
                    args.Add("-i");
                    args.Add(server.KeyPath!);
                    args.Add("-o");
                    args.Add("IdentitiesOnly=yes");
                }
                args.Add("-o");
                args.Add("PreferredAuthentications=publickey");
                break;

            case AuthMethod.Password:
                args.Add("-o");
                args.Add("PubkeyAuthentication=no");
                args.Add("-o");
                args.Add("PreferredAuthentications=password,keyboard-interactive");
                break;
        }

        if (server.Port is > 0 and not 22)
        {
            args.Add("-p");
            args.Add(server.Port.ToString());
        }

        args.AddRange(SplitExtraOptions(server.ExtraOptions));
        return args;
    }

    private static string Target(SshServer server)
        => string.IsNullOrWhiteSpace(server.User) ? server.Host : $"{server.User}@{server.Host}";

    /// <summary>Splits free-form extra options on whitespace, honouring double quotes.</summary>
    public static IEnumerable<string> SplitExtraOptions(string? extraOptions)
    {
        if (string.IsNullOrWhiteSpace(extraOptions)) yield break;

        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in extraOptions)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0) yield return current.ToString();
    }
}
