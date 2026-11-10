using System.Diagnostics;

namespace SshTunneler.Services;

/// <summary>One ssh port-forward process plus a rolling buffer of its diagnostic output.</summary>
public sealed class SshTunnelProcess : IDisposable
{
    private const int MaxLogLines = 500;

    private readonly Queue<string> _log = new();
    private readonly object _logLock = new();
    private Process? _process;

    public event EventHandler? Exited;

    public bool StoppedByUser { get; private set; }

    public int ExitCode { get; private set; }

    public bool IsRunning
    {
        get
        {
            var process = _process;
            try
            {
                return process is { HasExited: false };
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public void Start(string sshPath, IEnumerable<string> args, IDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo(sshPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true
        };

        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        foreach (var (key, value) in environment) startInfo.Environment[key] = value;

        AppendLog($"> ssh {string.Join(' ', startInfo.ArgumentList)}");

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog(e.Data); };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLog(e.Data); };
        _process.Exited += (_, _) =>
        {
            try { ExitCode = _process?.ExitCode ?? -1; } catch (InvalidOperationException) { ExitCode = -1; }
            Exited?.Invoke(this, EventArgs.Empty);
        };

        _process.Start();
        _process.BeginErrorReadLine();
        _process.BeginOutputReadLine();
    }

    public void Stop()
    {
        StoppedByUser = true;
        var process = _process;
        if (process is null) return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception)
        {
            // Already gone.
        }
    }

    public IReadOnlyList<string> GetLog()
    {
        lock (_logLock) return _log.ToArray();
    }

    /// <summary>Last meaningful ssh message, used as the failure reason in the UI.</summary>
    public string? GetFailureReason()
    {
        foreach (var line in GetLog().Reverse())
        {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith("debug", StringComparison.OrdinalIgnoreCase)) continue;
            if (text.StartsWith("> ssh", StringComparison.Ordinal)) continue;
            if (text.StartsWith("OpenSSH_", StringComparison.Ordinal)) continue;
            return SshMessages.Humanize(text);
        }

        return null;
    }

    private void AppendLog(string line)
    {
        lock (_logLock)
        {
            _log.Enqueue($"{DateTime.Now:HH:mm:ss} {line}");
            while (_log.Count > MaxLogLines) _log.Dequeue();
        }
    }

    public void Dispose()
    {
        Stop();
        _process?.Dispose();
        _process = null;
    }
}
