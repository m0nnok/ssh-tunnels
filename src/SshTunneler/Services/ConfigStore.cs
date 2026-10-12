using System.IO;
using System.Security.Cryptography;
using System.Text;
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
/// Stores the whole configuration as a single JSON blob encrypted with Windows DPAPI
/// (CurrentUser scope), so only the current Windows account can read it back.
/// </summary>
public sealed class DpapiJsonConfigStore : IConfigStore
{
    public const string BackupSuffix = ".bak";

    // Extra entropy so the blob cannot be decrypted by another app just by calling Unprotect.
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("SshTunneler.v1.config.entropy");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public DpapiJsonConfigStore(string? path = null)
    {
        Path = path ?? DefaultPath;

        // A relative path would be resolved against the working directory, which for anything
        // explorer.exe starts is C:\Windows\System32 — settings would silently read and write
        // somewhere nobody expects.
        if (!System.IO.Path.IsPathFullyQualified(Path))
            throw new ArgumentException($"Путь к файлу настроек должен быть абсолютным: '{Path}'.", nameof(path));
    }

    public string Path { get; }

    public static string DefaultPath =>
        System.IO.Path.Combine(RoamingAppDataDirectory(), "SshTunneler", "config.dat");

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

        throw new ConfigLoadException(
            "Файл настроек не читается: он повреждён или создан под другой учётной записью Windows.",
            Quarantine(),
            primary.Error);
    }

    public void Save(AppConfig config)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);
        var encrypted = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);

        // Atomic-ish write: full file first, then swap in, keeping the previous copy as .bak.
        var temp = Path + ".tmp";
        File.WriteAllBytes(temp, encrypted);
        if (File.Exists(Path))
            File.Replace(temp, Path, Path + BackupSuffix, ignoreMetadataErrors: true);
        else
            File.Move(temp, Path);
    }

    /// <summary>
    /// Reads one candidate file. A missing or empty file is not an error, it just has nothing to
    /// offer; <see cref="ReadResult.IsCorrupt"/> separates "could not decrypt it" from
    /// "could not even read the bytes", because only the former is worth quarantining.
    /// </summary>
    private static ReadResult Read(string path)
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
            var json = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return new ReadResult(JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig(), null, false);
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
