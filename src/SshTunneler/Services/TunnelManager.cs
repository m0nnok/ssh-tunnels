using System.Collections.Concurrent;
using Timer = System.Threading.Timer;
using SshTunneler.Models;

namespace SshTunneler.Services;

/// <summary>
/// Owns every running tunnel: starts and stops ssh.exe, tracks state, reconnects after drops.
/// Status changes are published on background threads, callers marshal them to the UI.
/// </summary>
public sealed class TunnelManager : IDisposable
{
    private static readonly int[] BackoffSeconds = [2, 5, 15, 30];

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly AskPassServer _askPass;
    private readonly string _sshPath = SshArgumentBuilder.ResolveSshPath();
    private readonly Timer _healthTimer;
    private AppConfig _config;
    private bool _disposed;

    public TunnelManager(AppConfig config, AskPassServer askPass)
    {
        _config = config;
        _askPass = askPass;
        _healthTimer = new Timer(_ => RunHealthCheck(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public event EventHandler<TunnelStatus>? StatusChanged;

    public string SshPath => _sshPath;

    public void UpdateConfig(AppConfig config) => _config = config;

    public TunnelStatus GetStatus(Guid resourceId)
        => _entries.TryGetValue(resourceId, out var entry) ? entry.Status : TunnelStatus.Idle(resourceId);

    /// <summary>Diagnostics survive the process, otherwise a failure would leave nothing to read.</summary>
    public IReadOnlyList<string> GetLog(Guid resourceId)
    {
        if (!_entries.TryGetValue(resourceId, out var entry)) return Array.Empty<string>();
        return entry.Process?.GetLog() ?? entry.LastLog;
    }

    public bool IsConnected(Guid resourceId) => GetStatus(resourceId).State == TunnelState.Connected;

    public IEnumerable<Guid> ActiveResourceIds
        => _entries.Where(kv => kv.Value.Status.State is TunnelState.Connected or TunnelState.Connecting)
                   .Select(kv => kv.Key);

    public async Task ToggleAsync(Guid resourceId)
    {
        var state = GetStatus(resourceId).State;
        if (state is TunnelState.Connected or TunnelState.Connecting or TunnelState.Reconnecting)
            await DisconnectAsync(resourceId);
        else
            await ConnectAsync(resourceId);
    }

    public async Task<bool> ConnectAsync(Guid resourceId)
    {
        var entry = _entries.GetOrAdd(resourceId, id => new Entry(id));
        await entry.Gate.WaitAsync();
        try
        {
            if (entry.Process is { IsRunning: true }) return true;

            entry.ManualStop = false;
            CleanupProcess(entry);

            var resource = _config.FindResource(resourceId);
            if (resource is null) return Fail(entry, "Ресурс не найден в настройках");

            var server = _config.FindServer(resource.ServerId);
            if (server is null) return Fail(entry, "Для ресурса не выбран сервер");
            if (string.IsNullOrWhiteSpace(server.Host)) return Fail(entry, "У сервера не заполнен адрес");

            var port = ResolveLocalPort(resource, out var portError);
            if (port == 0) return Fail(entry, portError!);

            SetStatus(entry, new TunnelStatus(resourceId, TunnelState.Connecting, port, "Подключение...", DateTime.Now));

            var process = new SshTunnelProcess();
            entry.Process = process;
            entry.AskPassToken = _askPass.Register(_ => server.Secret);
            process.Exited += (_, _) => OnProcessExited(entry, process);

            try
            {
                process.Start(
                    _sshPath,
                    SshArgumentBuilder.BuildTunnelArgs(server, resource, port),
                    BuildEnvironment(entry.AskPassToken!, server.ToString()));
            }
            catch (Exception ex)
            {
                return Fail(entry, $"Не удалось запустить ssh.exe: {ex.Message}");
            }

            if (!await WaitForListenerAsync(process, port))
            {
                var reason = process.GetFailureReason() ?? "Туннель не поднялся за отведённое время";
                process.Stop();
                return Fail(entry, reason);
            }

            entry.Attempt = 0;
            SetStatus(entry, new TunnelStatus(resourceId, TunnelState.Connected, port, null, DateTime.Now));
            return true;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task DisconnectAsync(Guid resourceId)
    {
        if (!_entries.TryGetValue(resourceId, out var entry)) return;

        entry.ManualStop = true;
        entry.Cancellation?.Cancel();

        await entry.Gate.WaitAsync();
        try
        {
            CleanupProcess(entry);
            SetStatus(entry, TunnelStatus.Idle(resourceId));
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task DisconnectAllAsync()
    {
        foreach (var id in _entries.Keys.ToArray())
            await DisconnectAsync(id);
    }

    /// <summary>Closes tunnels whose resource has been deleted, so no ssh process is left orphaned.</summary>
    public async Task DisconnectRemovedAsync()
    {
        foreach (var id in _entries.Keys.ToArray())
            if (_config.FindResource(id) is null)
                await DisconnectAsync(id);
    }

    private Dictionary<string, string> BuildEnvironment(string token, string label) => new()
    {
        // ssh.exe cannot read secrets from stdin, so it asks this very executable instead.
        ["SSH_ASKPASS"] = Environment.ProcessPath ?? "",
        ["SSH_ASKPASS_REQUIRE"] = "force",
        ["DISPLAY"] = "localhost:0",
        [AskPassProtocol.PipeEnvVar] = _askPass.PipeName,
        [AskPassProtocol.TokenEnvVar] = token,
        [AskPassProtocol.LabelEnvVar] = label
    };

    private int ResolveLocalPort(TunnelResource resource, out string? error)
    {
        error = null;

        if (resource.LocalPort > 0)
        {
            if (!PortAllocator.IsPortFree(resource.LocalPort))
            {
                error = $"Локальный порт {resource.LocalPort} уже занят";
                return 0;
            }

            return resource.LocalPort;
        }

        var reserved = _entries.Values
            .Where(e => e.Status.LocalPort > 0 && e.Status.State != TunnelState.Idle)
            .Select(e => e.Status.LocalPort);

        var port = PortAllocator.Allocate(_config.Ui.LocalPortRangeStart, _config.Ui.LocalPortRangeEnd, reserved);
        if (port == 0) error = "В заданном диапазоне нет свободных локальных портов";
        return port;
    }

    /// <summary>Tunnel is up once ssh has bound the local port and the process is still alive.</summary>
    private static async Task<bool> WaitForListenerAsync(SshTunnelProcess process, int port)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (!process.IsRunning) return false;
            if (!PortAllocator.IsPortFree(port)) return true;
            await Task.Delay(250);
        }

        return false;
    }

    private void OnProcessExited(Entry entry, SshTunnelProcess process)
    {
        if (entry.ManualStop || process.StoppedByUser) return;
        if (!ReferenceEquals(entry.Process, process)) return;
        if (entry.Status.State is TunnelState.Idle) return;

        var reason = process.GetFailureReason() ?? "Соединение разорвано";
        SetStatus(entry, new TunnelStatus(entry.ResourceId, TunnelState.Failed, 0, reason, DateTime.Now));

        if (_config.Ui.AutoReconnect && !_disposed)
            ScheduleReconnect(entry, reason);
    }

    private void ScheduleReconnect(Entry entry, string reason)
    {
        var delay = BackoffSeconds[Math.Min(entry.Attempt, BackoffSeconds.Length - 1)];
        entry.Attempt++;

        entry.Cancellation?.Cancel();
        entry.Cancellation = new CancellationTokenSource();
        var token = entry.Cancellation.Token;

        SetStatus(entry, new TunnelStatus(
            entry.ResourceId, TunnelState.Reconnecting, 0,
            $"{reason}. Переподключение через {delay} с", DateTime.Now));

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), token);
                if (!token.IsCancellationRequested && !entry.ManualStop)
                    await ConnectAsync(entry.ResourceId);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a manual action.
            }
        });
    }

    private void RunHealthCheck()
    {
        if (_disposed) return;

        foreach (var entry in _entries.Values)
        {
            if (entry.Status.State != TunnelState.Connected) continue;

            var process = entry.Process;
            if (process is null || !process.IsRunning)
            {
                // The Exited event covers the usual case, this catches whatever it missed.
                if (!entry.ManualStop)
                    SetStatus(entry, new TunnelStatus(
                        entry.ResourceId, TunnelState.Failed, 0,
                        process?.GetFailureReason() ?? "Процесс ssh завершился", DateTime.Now));
                continue;
            }

            if (PortAllocator.IsPortFree(entry.Status.LocalPort))
                SetStatus(entry, new TunnelStatus(
                    entry.ResourceId, TunnelState.Failed, 0, "Локальный порт больше не слушается", DateTime.Now));
        }
    }

    private bool Fail(Entry entry, string message)
    {
        CleanupProcess(entry);
        SetStatus(entry, new TunnelStatus(entry.ResourceId, TunnelState.Failed, 0, message, DateTime.Now));
        return false;
    }

    private void CleanupProcess(Entry entry)
    {
        if (entry.AskPassToken is not null)
        {
            _askPass.Unregister(entry.AskPassToken);
            entry.AskPassToken = null;
        }

        if (entry.Process is not null)
        {
            entry.LastLog = entry.Process.GetLog();
            entry.Process.Dispose();
            entry.Process = null;
        }
    }

    private void SetStatus(Entry entry, TunnelStatus status)
    {
        entry.Status = status;
        StatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _healthTimer.Dispose();
        foreach (var entry in _entries.Values)
        {
            entry.Cancellation?.Cancel();
            entry.Process?.Dispose();
        }

        _entries.Clear();
    }

    private sealed class Entry(Guid resourceId)
    {
        public Guid ResourceId { get; } = resourceId;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public SshTunnelProcess? Process { get; set; }
        public string? AskPassToken { get; set; }
        public TunnelStatus Status { get; set; } = TunnelStatus.Idle(resourceId);
        public int Attempt { get; set; }
        public bool ManualStop { get; set; }
        public IReadOnlyList<string> LastLog { get; set; } = Array.Empty<string>();
        public CancellationTokenSource? Cancellation { get; set; }
    }
}
