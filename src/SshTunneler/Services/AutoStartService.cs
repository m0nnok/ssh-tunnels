using Microsoft.Win32;

namespace SshTunneler.Services;

/// <summary>Per-user autostart through the Run key, no admin rights needed.</summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SshTunneler";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    /// <summary>Brings the registry in line with the setting, ignoring registry hiccups.</summary>
    public static void Sync(bool enabled)
    {
        try
        {
            if (IsEnabled() != enabled) SetEnabled(enabled);
        }
        catch (Exception)
        {
            // Autostart is a convenience, never a reason to fail a save or a startup.
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key is null) return;

        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            key.SetValue(ValueName, $"\"{exe}\" --minimized");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
