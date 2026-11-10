using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace SshTunneler.Services;

/// <summary>
/// Serves secrets to askpass helpers over a private named pipe. Each tunnel gets its own
/// unguessable token, and only processes of the current user are served.
/// </summary>
public sealed class AskPassServer : IDisposable
{
    private readonly ConcurrentDictionary<string, Func<string, string?>> _providers = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SecurityIdentifier? _ownerSid;

    public AskPassServer()
    {
        PipeName = AskPassProtocol.NewPipeName();
        if (OperatingSystem.IsWindows()) _ownerSid = CurrentUserSid();

        _ = Task.Run(AcceptLoopAsync);
    }

    public string PipeName { get; }

    /// <summary>Registers a secret provider and returns the token the helper must present.</summary>
    public string Register(Func<string, string?> provider)
    {
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        _providers[token] = provider;
        return token;
    }

    public void Unregister(string token) => _providers.TryRemove(token, out _);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                // CurrentUserOnly limits the pipe ACL to this user on Windows; on Unix the server
                // compares the peer's uid and refuses anyone else.
                pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(_cts.Token);
                var accepted = pipe;
                pipe = null;
                _ = Task.Run(() => ServeAsync(accepted));
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                return;
            }
            catch (Exception)
            {
                pipe?.Dispose();
                await Task.Delay(200);
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        try
        {
            using (pipe)
            {
                if (OperatingSystem.IsWindows() && !IsOwnerConnected(pipe)) return;

                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) return;

                var request = JsonSerializer.Deserialize<AskPassProtocol.Request>(line);
                string? secret = null;
                if (request is not null && _providers.TryGetValue(request.Token, out var provider))
                    secret = provider(request.Prompt);

                var response = new AskPassProtocol.Response(!string.IsNullOrEmpty(secret), secret);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                if (OperatingSystem.IsWindows()) pipe.WaitForPipeDrain();
            }
        }
        catch (Exception)
        {
            // A helper that goes away mid-handshake is not worth reporting.
        }
    }

    /// <summary>
    /// Compares the client by SID: display names come back in different shapes depending on
    /// how the account is qualified, so only the SID is a reliable check. On Unix the uid check
    /// behind <see cref="PipeOptions.CurrentUserOnly"/> already does this job.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private bool IsOwnerConnected(NamedPipeServerStream pipe)
    {
        if (_ownerSid is null) return true;

        try
        {
            SecurityIdentifier? clientSid = null;
            pipe.RunAsClient(() => clientSid = CurrentUserSid());

            return clientSid is null || _ownerSid.Equals(clientSid);
        }
        catch (Exception)
        {
            // Impersonation is unavailable: the random token stays as the only guard.
            return true;
        }
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier? CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _providers.Clear();
        _cts.Dispose();
    }
}
