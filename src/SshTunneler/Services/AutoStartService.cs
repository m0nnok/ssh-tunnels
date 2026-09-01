using Microsoft.Win32;

namespace SshTunneler.Services;

/// <summary>Per-user autostart through the Run key, no admin rights needed.</summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SshTunneler";

    public static bool IsEnabled() => CurrentValue() is { Length: > 0 };

    private static string? CurrentValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    /// <summary>
    /// Brings the registry in line with the setting, ignoring registry hiccups. Also repoints a
    /// stale entry at the running executable, so autostart survives moving or installing the app.
    /// </summary>
    public static void Sync(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (IsEnabled()) SetEnabled(false);
                return;
            }

            if (CurrentValue() != ExpectedValue()) SetEnabled(true);
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

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key is null) return;

        if (enabled)
        {
            if (ExpectedValue() is not { } command) return;
            key.SetValue(ValueName, command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
