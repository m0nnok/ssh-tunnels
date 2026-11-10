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
        if (!string.IsNullOrWhiteSpace(browserPath))
        {
            if (OperatingSystem.IsMacOS())
            {
                if (TryOpenWithMacApp(url, browserPath)) return;
            }
            else if (File.Exists(browserPath))
            {
                Process.Start(new ProcessStartInfo(browserPath, url) { UseShellExecute = true });
                return;
            }
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>
    /// A macOS browser is an .app folder or just an application name, and open -a takes both.
    /// An unknown application makes open fail at once, and then the default browser is used.
    /// </summary>
    private static bool TryOpenWithMacApp(string url, string browser)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add(browser);
        startInfo.ArgumentList.Add(url);

        using var process = Process.Start(startInfo);
        return process is not null && process.WaitForExit(3000) && process.ExitCode == 0;
    }
}
