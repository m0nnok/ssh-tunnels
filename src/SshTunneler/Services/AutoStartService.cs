using System.IO;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace SshTunneler.Services;

/// <summary>
/// Per-user autostart, no admin rights needed: the Run key on Windows, a LaunchAgent on macOS.
/// </summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SshTunneler";

    /// <summary>LaunchAgent label, also the bundle identifier of the macOS app.</summary>
    public const string LaunchAgentLabel = "io.github.m0nnok.sshtunneler";

    public static string LaunchAgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", LaunchAgentLabel + ".plist");

    /// <summary>
    /// Brings the system in line with the setting, ignoring hiccups. Also repoints a stale entry
    /// at the running executable, so autostart survives moving or installing the app.
    /// </summary>
    public static void Sync(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                SyncRunKey(enabled);
            else if (OperatingSystem.IsMacOS())
                SyncLaunchAgent(enabled);
        }
        catch (Exception)
        {
            // Autostart is a convenience, never a reason to fail a save or a startup.
        }
    }

    /// <summary>The command the Run entry should hold for the executable running right now.</summary>
    public static string? ExpectedValue()
    {
        var exe = Environment.ProcessPath;
        return string.IsNullOrEmpty(exe) ? null : $"\"{exe}\" --minimized";
    }

    /// <summary>
    /// LaunchAgent that starts the executable hidden at login. The executable inside the .app is
    /// started directly, so the --minimized argument survives, which `open -a` would drop.
    /// </summary>
    public static string BuildLaunchAgentPlist(string executablePath) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>{LaunchAgentLabel}</string>
            <key>ProgramArguments</key>
            <array>
                <string>{SecurityElement.Escape(executablePath)}</string>
                <string>--minimized</string>
            </array>
            <key>RunAtLoad</key>
            <true/>
            <key>LimitLoadToSessionType</key>
            <string>Aqua</string>
            <key>ProcessType</key>
            <string>Interactive</string>
            <key>AssociatedBundleIdentifiers</key>
            <string>{LaunchAgentLabel}</string>
        </dict>
        </plist>

        """;

    /// <summary>
    /// True for a copy started straight from the mounted disk image, or one macOS translocated
    /// because it still carries the download quarantine: that path is gone after eject or
    /// restart, so an autostart entry pointing at it would start nothing.
    /// </summary>
    public static bool IsTransientLocation(string executablePath) =>
        executablePath.StartsWith("/Volumes/SSH Tunneler/", StringComparison.Ordinal) ||
        executablePath.Contains("/AppTranslocation/", StringComparison.Ordinal);

    [SupportedOSPlatform("windows")]
    private static void SyncRunKey(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key is null) return;

        var current = key.GetValue(ValueName) as string;
        if (!enabled)
        {
            if (current is { Length: > 0 }) key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        if (ExpectedValue() is { } command && current != command) key.SetValue(ValueName, command);
    }

    private static void SyncLaunchAgent(bool enabled)
    {
        var path = LaunchAgentPath;
        if (!enabled)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        if (Environment.ProcessPath is not { Length: > 0 } exe || IsTransientLocation(exe)) return;

        // launchd only reads the file at the next login, which is exactly when it is needed.
        var expected = BuildLaunchAgentPlist(exe);
        if (File.Exists(path) && File.ReadAllText(path) == expected) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, expected);
    }
}
