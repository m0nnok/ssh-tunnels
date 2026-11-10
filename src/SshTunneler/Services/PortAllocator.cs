using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SshTunneler.Services;

/// <summary>Local port bookkeeping: what is taken and which free port to bind next.</summary>
public static class PortAllocator
{
    public static bool IsPortFree(int port)
    {
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            foreach (var listener in properties.GetActiveTcpListeners())
                if (listener.Port == port) return false;
            return true;
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            // Fall back to actually trying to bind.
            return TryBind(port);
        }
    }

    private static bool TryBind(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>First free port in the range, or 0 when the whole range is taken.</summary>
    public static int Allocate(int rangeStart, int rangeEnd, IEnumerable<int>? reserved = null)
    {
        if (rangeStart < 1 || rangeStart > 65535) rangeStart = 20000;
        if (rangeEnd < rangeStart || rangeEnd > 65535) rangeEnd = Math.Min(rangeStart + 999, 65535);

        var taken = reserved is null ? new HashSet<int>() : new HashSet<int>(reserved);
        for (var port = rangeStart; port <= rangeEnd; port++)
        {
            if (taken.Contains(port)) continue;
            if (IsPortFree(port)) return port;
        }

        return 0;
    }

    /// <summary>True when something accepts a TCP connection on the loopback port.</summary>
    public static async Task<bool> IsListeningAsync(int port, int timeoutMs, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMs);
            await client.ConnectAsync(IPAddress.Loopback, port, timeoutCts.Token);
            return client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
