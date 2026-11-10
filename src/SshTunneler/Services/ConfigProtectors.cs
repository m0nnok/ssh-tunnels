using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace SshTunneler.Services;

/// <summary>Encrypts the configuration blob so that only the current user can read it back.</summary>
public interface IConfigProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

/// <summary>
/// The key exists but cannot be reached right now (keychain locked, access denied). Unlike a
/// failed decryption this says nothing about the file, so the file must not be quarantined.
/// </summary>
public sealed class ConfigKeyUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

public static class ConfigProtectors
{
    public static IConfigProtector ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return new DpapiConfigProtector();
        if (OperatingSystem.IsMacOS()) return new KeychainConfigProtector();
        throw new PlatformNotSupportedException("Хранение настроек поддерживается только на Windows и macOS.");
    }
}

/// <summary>Windows DPAPI, CurrentUser scope: only the current Windows account can decrypt.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiConfigProtector : IConfigProtector
{
    // Extra entropy so the blob cannot be decrypted by another app just by calling Unprotect.
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("SshTunneler.v1.config.entropy");

    public byte[] Protect(byte[] data) =>
        ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) =>
        ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>
/// AES-GCM with a random 256-bit key stored in the user's login keychain, the macOS counterpart
/// of DPAPI. The file layout is nonce | tag | ciphertext.
/// </summary>
/// <remarks>
/// The keychain is driven through /usr/bin/security rather than the Security framework: a keychain
/// item trusts the binary that created it, and an ad-hoc signed app gets a new identity with every
/// build, so a direct call would make macOS ask for keychain access after each update. The key is
/// handed to security(1) on stdin, never on the command line.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class KeychainConfigProtector : IConfigProtector
{
    public const string Service = "SshTunneler";
    public const string Account = "config-key";

    private const string SecurityTool = "/usr/bin/security";
    private const int ItemNotFoundExitCode = 44; // errSecItemNotFound as reported by security(1)
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("SshTunneler.v1.config");

    private readonly string? _keychainPath;
    private readonly Lock _keyLock = new();
    private byte[]? _key;

    /// <param name="keychainPath">A specific keychain file instead of the default one, for tests.</param>
    public KeychainConfigProtector(string? keychainPath = null)
    {
        // security -i splits its input on whitespace, so a path with spaces cannot be passed safely.
        if (keychainPath is not null && keychainPath.Any(char.IsWhiteSpace))
            throw new ArgumentException("Путь к связке ключей не должен содержать пробелов.", nameof(keychainPath));

        _keychainPath = keychainPath;
    }

    public byte[] Protect(byte[] data)
    {
        var key = GetKey(create: true)!;
        var output = new byte[NonceSize + TagSize + data.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), AssociatedData);
        return output;
    }

    public byte[] Unprotect(byte[] data)
    {
        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("Файл слишком короткий для зашифрованных данных.");

        // A missing key means the file can never be decrypted, which makes it as good as corrupt.
        var key = GetKey(create: false)
                  ?? throw new CryptographicException("Ключ шифрования не найден в связке ключей.");

        var plain = new byte[data.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            data.AsSpan(0, NonceSize),
            data.AsSpan(NonceSize + TagSize),
            data.AsSpan(NonceSize, TagSize),
            plain,
            AssociatedData);
        return plain;
    }

    private byte[]? GetKey(bool create)
    {
        lock (_keyLock)
        {
            if (_key is not null) return _key;

            var key = ReadKey();
            if (key is null && create) key = AddKey();
            return _key = key;
        }
    }

    private byte[]? ReadKey()
    {
        List<string> args = ["find-generic-password", "-s", Service, "-a", Account, "-w"];
        if (_keychainPath is not null) args.Add(_keychainPath);

        var result = RunSecurity(args, input: null);
        if (result.ExitCode == ItemNotFoundExitCode) return null;
        if (result.ExitCode != 0)
            throw new ConfigKeyUnavailableException(
                $"Связка ключей недоступна (security, код {result.ExitCode}): {result.Error.Trim()}");

        try
        {
            var key = Convert.FromHexString(result.Output.Trim());
            if (key.Length == KeySize) return key;
        }
        catch (FormatException)
        {
            // Reported below.
        }

        throw new CryptographicException("Ключ шифрования в связке ключей повреждён.");
    }

    private byte[] AddKey()
    {
        var key = RandomNumberGenerator.GetBytes(KeySize);
        var command = $"add-generic-password -s {Service} -a {Account} -w {Convert.ToHexString(key)}";
        if (_keychainPath is not null) command += " " + _keychainPath;

        // In interactive mode security(1) reads the command from stdin, keeping the key out of
        // the process list. Its exit code does not reflect the command, so read the key back.
        RunSecurity(["-i"], command + "\n");

        return ReadKey() ?? throw new ConfigKeyUnavailableException(
            "Не удалось сохранить ключ шифрования настроек в связке ключей.");
    }

    private static (int ExitCode, string Output, string Error) RunSecurity(IEnumerable<string> args, string? input)
    {
        var startInfo = new ProcessStartInfo(SecurityTool)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
                            ?? throw new ConfigKeyUnavailableException("Не удалось запустить security.");

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (input is not null) process.StandardInput.Write(input);
        process.StandardInput.Close();

        // A locked keychain makes macOS ask for the login password, which takes a human.
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            try { process.Kill(); } catch (Exception) { }
            throw new ConfigKeyUnavailableException("Связка ключей не ответила вовремя.");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
