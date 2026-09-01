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
    // Extra entropy so the blob cannot be decrypted by another app just by calling Unprotect.
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("SshTunneler.v1.config.entropy");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public DpapiJsonConfigStore(string? path = null)
        => Path = path ?? DefaultPath;

    public string Path { get; }

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SshTunneler",
        "config.dat");

    public AppConfig Load()
    {
        if (!File.Exists(Path)) return new AppConfig();

        byte[] encrypted;
        try
        {
            encrypted = File.ReadAllBytes(Path);
        }
        catch (Exception ex)
        {
            throw new ConfigLoadException($"Не удалось прочитать файл настроек: {ex.Message}", null, ex);
        }

        if (encrypted.Length == 0) return new AppConfig();

        try
        {
            var json = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            var quarantined = Quarantine();
            throw new ConfigLoadException(
                "Файл настроек не читается: он повреждён или создан под другой учётной записью Windows.",
                quarantined,
                ex);
        }
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
            File.Replace(temp, Path, Path + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(temp, Path);
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
}
