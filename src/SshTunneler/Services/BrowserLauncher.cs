using System.IO;
using System.Diagnostics;
using SshTunneler.Models;

namespace SshTunneler.Services;

public static class BrowserLauncher
{
    /// <summary>Expands the resource URL template against the port the tunnel actually got.</summary>
    public static string BuildUrl(TunnelResource resource, int localPort)
    {
        var template = string.IsNullOrWhiteSpace(resource.UrlTemplate)
            ? "http://127.0.0.1:{localPort}"
            : resource.UrlTemplate;

        return template
            .Replace("{localPort}", localPort.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{remotePort}", resource.RemotePort.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{remoteHost}", resource.RemoteHost, StringComparison.OrdinalIgnoreCase);
    }

    public static void Open(string url, string? browserPath)
    {
        if (!string.IsNullOrWhiteSpace(browserPath) && File.Exists(browserPath))
        {
            Process.Start(new ProcessStartInfo(browserPath, url) { UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
