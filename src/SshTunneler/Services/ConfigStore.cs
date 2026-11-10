using System.IO;
using System.Text.Json;
using SshTunneler.Models;

namespace SshTunneler.Services;

public sealed class ConfigLoadException : Exception
{
    public ConfigLoadException(string message, string? quarantinedPath, Exception? inner = null)
        : base(message, inner) => QuarantinedPath = quarantinedPath;

    /// <summary>Where the unreadable file was moved to, when it was moved.</summary>
    public string? QuarantinedPath { get; }
}

public interface IConfigStore
{
    string Path { get; }
    AppConfig Load();
    void Save(AppConfig config);
}

/// <summary>
/// Stores the whole configuration as a single JSON blob encrypted for the current user:
/// Windows DPAPI on Windows, an AES key kept in the login keychain on macOS.
/// </summary>
public sealed class EncryptedJsonConfigStore : IConfigStore
{
    public const string BackupSuffix = ".bak";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IConfigProtector _protector;

    public EncryptedJsonConfigStore(IConfigProtector protector, string? path = null)
    {
        _protector = protector;
        Path = path ?? DefaultPath;

        // A relative path would be resolved against the working directory, which for anything
        // explorer.exe starts is C:\Windows\System32 and for launchd is / — settings would
        // silently read and write somewhere nobody expects.
        if (!System.IO.Path.IsPathFullyQualified(Path))
            throw new ArgumentException($"Путь к файлу настроек должен быть абсолютным: '{Path}'.", nameof(path));
    }

    public string Path { get; }

    /// <summary>The store the app uses: default location, protector of the current OS.</summary>
    public static EncryptedJsonConfigStore CreateDefault() => new(ConfigProtectors.ForCurrentPlatform());

    public static string DefaultPath =>
        System.IO.Path.Combine(UserDataDirectory(), "SshTunneler", "config.dat");

    /// <summary>Per-user settings folder: roaming AppData on Windows, Application Support on macOS.</summary>
    public static string UserDataDirectory()
    {
        if (OperatingSystem.IsWindows()) return RoamingAppDataDirectory();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
            return System.IO.Path.Combine(home, "Library", "Application Support");

        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) is { Length: > 0 } config
            ? config
            : System.IO.Path.Combine(home, ".config");
    }

    /// <summary>
    /// Roaming AppData, resolved without trusting a single lookup. For
    /// <see cref="Environment.SpecialFolder.ApplicationData"/> the shell verifies the folder and
    /// hands back an empty string when that check fails, which happens while the profile is still
    /// settling right after logon — exactly when the Run key starts this app. An empty base turns
    /// the config path into a relative one, the real file is never found, and the app comes up as
    /// if every setting had been wiped.
    /// </summary>
    public static string RoamingAppDataDirectory()
    {
        foreach (var candidate in Candidates())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && System.IO.Path.IsPathFullyQualified(candidate))
                return candidate;
        }

        throw new InvalidOperationException(
            "Не удалось определить папку %APPDATA% для хранения настроек.");

        static IEnumerable<string?> Candidates()
        {
            yield return Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
            yield return Environment.GetEnvironmentVariable("APPDATA");

            var profile = Environment.GetEnvironmentVariable("USERPROFILE");
            yield return string.IsNullOrEmpty(profile)
                ? null
                : System.IO.Path.Combine(profile, "AppData", "Roaming");
        }
    }

    public AppConfig Load()
    {
        var primary = Read(Path);
        if (primary.Config is not null) return primary.Config;

        // The main file is missing or unreadable, but Save always leaves the previous good copy
        // behind, so fall back to it instead of starting the user from scratch.
        var backup = Read(Path + BackupSuffix);
        if (backup.Config is not null)
        {
            if (primary.IsCorrupt) Quarantine();
            return backup.Config;
        }

        if (primary.Error is null) return new AppConfig();

        if (!primary.IsCorrupt)
            throw new ConfigLoadException(
                $"Не удалось прочитать файл настроек: {primary.Error.Message}", null, primary.Error);

        throw new ConfigLoadException(UnreadableMessage, Quarantine(), primary.Error);
    }

    public void Save(AppConfig config)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);
        var encrypted = _protector.Protect(json);

        // Atomic-ish write: full file first, then swap in, keeping the previous copy as .bak.
        var temp = Path + ".tmp";
        File.WriteAllBytes(temp, encrypted);
        if (File.Exists(Path))
            File.Replace(temp, Path, Path + BackupSuffix, ignoreMetadataErrors: true);
        else
            File.Move(temp, Path);
    }

    /// <summary>
    /// Moves the settings file and its backup aside under a dated name. Used when the file is
    /// in place but cannot be read right now (keychain locked, access denied): the app then runs
    /// on an empty configuration, and its saves must not overwrite what could not be read.
    /// Returns the new path of the main file, or null when the files could not be moved.
    /// </summary>
    public string? SetAside()
    {
        var target = $"{Path}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            if (File.Exists(Path + BackupSuffix)) File.Move(Path + BackupSuffix, target + BackupSuffix);
            if (File.Exists(Path)) File.Move(Path, target);
            return target;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string UnreadableMessage => OperatingSystem.IsWindows()
        ? "Файл настроек не читается: он повреждён или создан под другой учётной записью Windows."
        : "Файл настроек не читается: он повреждён или его ключ шифрования отсутствует в связке ключей.";

    /// <summary>
    /// Reads one candidate file. A missing or empty file is not an error, it just has nothing to
    /// offer; <see cref="ReadResult.IsCorrupt"/> separates "could not decrypt it" from
    /// "could not even read the bytes or reach the key", because only the former is worth
    /// quarantining.
    /// </summary>
    private ReadResult Read(string path)
    {
        if (!File.Exists(path)) return default;

        byte[] encrypted;
        try
        {
            encrypted = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            return new ReadResult(null, ex, IsCorrupt: false);
        }

        if (encrypted.Length == 0) return default;

        try
        {
            var json = _protector.Unprotect(encrypted);
            return new ReadResult(JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig(), null, false);
        }
        catch (ConfigKeyUnavailableException ex)
        {
            // The keychain said no or is locked: the file may be perfectly fine.
            return new ReadResult(null, ex, IsCorrupt: false);
        }
        catch (Exception ex)
        {
            return new ReadResult(null, ex, IsCorrupt: true);
        }
    }

    private string? Quarantine()
    {
        try
        {
            var target = $"{Path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(Path, target);
            return target;
        }
        catch
        {
            return null;
        }
    }

    private readonly record struct ReadResult(AppConfig? Config, Exception? Error, bool IsCorrupt);
}
