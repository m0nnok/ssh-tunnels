using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using SshTunneler.Models;
using SshTunneler.Services;
using Xunit;

namespace SshTunneler.Tests;

/// <summary>Store logic: atomic saves, the .bak fallback, quarantine. Encryption is faked here.</summary>
public class ConfigStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "SshTunnelerTests", Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_directory, "config.dat");

    private EncryptedJsonConfigStore Store(IConfigProtector? protector = null)
        => new(protector ?? new FakeProtector(), ConfigPath);

    [Fact]
    public void SaveThenLoad_RoundTripsServersResourcesAndSecrets()
    {
        var config = new AppConfig();
        var server = new SshServer
        {
            Name = "prod",
            Host = "10.0.0.5",
            User = "deploy",
            AuthMethod = AuthMethod.Password,
            Secret = "s3cret"
        };
        config.Servers.Add(server);
        config.Resources.Add(new TunnelResource
        {
            Name = "grafana",
            ServerId = server.Id,
            RemotePort = 3000,
            LocalPort = 20010
        });
        config.Ui.Opacity = 0.5;

        Store().Save(config);
        var loaded = Store().Load();

        Assert.Equal("prod", loaded.Servers.Single().Name);
        Assert.Equal("s3cret", loaded.Servers.Single().Secret);
        Assert.Equal(server.Id, loaded.Resources.Single().ServerId);
        Assert.Equal(3000, loaded.Resources.Single().RemotePort);
        Assert.Equal(0.5, loaded.Ui.Opacity);
    }

    [Fact]
    public void Load_OnMissingFile_ReturnsEmptyConfiguration()
    {
        var loaded = Store().Load();

        Assert.Empty(loaded.Servers);
        Assert.Empty(loaded.Resources);
    }

    [Fact]
    public void Load_OnUnreadableFile_ThrowsAndQuarantinesIt()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath, "this is not an encrypted blob");

        var ex = Assert.Throws<ConfigLoadException>(() => Store().Load());

        Assert.NotNull(ex.QuarantinedPath);
        Assert.True(File.Exists(ex.QuarantinedPath!));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public void Load_WhenTheKeyIsUnreachable_LeavesTheFileAlone()
    {
        var config = new AppConfig();
        config.Servers.Add(new SshServer { Name = "prod", Host = "10.0.0.5" });
        Store().Save(config);

        // A locked or denied keychain says nothing about the file, which must survive untouched.
        var ex = Assert.Throws<ConfigLoadException>(() => Store(new UnreachableKeyProtector()).Load());

        Assert.Null(ex.QuarantinedPath);
        Assert.True(File.Exists(ConfigPath));
        Assert.Equal("prod", Store().Load().Servers.Single().Name);
    }

    [Fact]
    public void SetAside_KeepsAnUnreadableFileAndItsBackupSafeFromLaterSaves()
    {
        var store = Store();
        var config = new AppConfig();
        config.Servers.Add(new SshServer { Name = "prod", Host = "10.0.0.5" });
        store.Save(config);
        store.Save(config); // second save produces the .bak copy

        var setAside = store.SetAside();

        // The app then runs on an empty configuration; its saves must not reach the old files.
        store.Save(new AppConfig());
        store.Save(new AppConfig());

        Assert.NotNull(setAside);
        Assert.True(File.Exists(setAside + EncryptedJsonConfigStore.BackupSuffix));
        Assert.Equal("prod", new EncryptedJsonConfigStore(new FakeProtector(), setAside).Load().Servers.Single().Name);
        Assert.Empty(Store().Load().Servers);
    }

    [Fact]
    public void SecondSave_KeepsThePreviousVersionAsBackup()
    {
        var store = Store();
        var config = new AppConfig();
        store.Save(config);

        config.Servers.Add(new SshServer { Name = "prod", Host = "10.0.0.5" });
        store.Save(config);

        Assert.True(File.Exists(ConfigPath + ".bak"));
        Assert.False(File.Exists(ConfigPath + ".tmp"));
        Assert.Single(Store().Load().Servers);
    }

    [Fact]
    public void Load_WhenMainFileIsMissing_FallsBackToThePreviousSave()
    {
        var store = Store();
        var config = new AppConfig();
        config.Servers.Add(new SshServer { Name = "prod", Host = "10.0.0.5" });
        store.Save(config);
        config.Servers.Add(new SshServer { Name = "staging", Host = "10.0.0.6" });
        store.Save(config);

        File.Delete(ConfigPath);

        // The backup trails the current file by one save, so this is the state before "staging".
        Assert.Equal("prod", Store().Load().Servers.Single().Name);
    }

    [Fact]
    public void Load_WhenMainFileIsUnreadable_RecoversFromBackupAndQuarantinesIt()
    {
        var store = Store();
        var config = new AppConfig();
        config.Servers.Add(new SshServer { Name = "prod", Host = "10.0.0.5" });
        store.Save(config);
        store.Save(config); // second save produces the .bak copy

        File.WriteAllText(ConfigPath, "this is not an encrypted blob");

        Assert.Equal("prod", Store().Load().Servers.Single().Name);
        Assert.False(File.Exists(ConfigPath));
        Assert.Single(Directory.GetFiles(_directory, "config.dat.corrupt-*"));
    }

    [Fact]
    public void Constructor_RejectsARelativePath()
    {
        // A relative path resolves against the working directory, which for anything started from
        // the Run key is C:\Windows\System32 — the saved settings would look wiped.
        Assert.Throws<ArgumentException>(() => new EncryptedJsonConfigStore(new FakeProtector(), @"SshTunneler\config.dat"));
    }

    [Fact]
    public void DefaultPath_IsAbsolute()
    {
        Assert.True(Path.IsPathFullyQualified(EncryptedJsonConfigStore.UserDataDirectory()));
        Assert.True(Path.IsPathFullyQualified(EncryptedJsonConfigStore.DefaultPath));
        if (OperatingSystem.IsWindows())
            Assert.True(Path.IsPathFullyQualified(EncryptedJsonConfigStore.RoamingAppDataDirectory()));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best effort.
        }
    }

    /// <summary>Reversible scrambling with a marker, so foreign bytes fail like a bad decryption.</summary>
    private sealed class FakeProtector : IConfigProtector
    {
        private static readonly byte[] Marker = "FAKE"u8.ToArray();

        public byte[] Protect(byte[] data) => [.. Marker, .. data.Select(b => (byte)(b ^ 0x5A))];

        public byte[] Unprotect(byte[] data)
        {
            if (!data.AsSpan().StartsWith(Marker)) throw new CryptographicException("not a fake blob");
            return data[Marker.Length..].Select(b => (byte)(b ^ 0x5A)).ToArray();
        }
    }

    private sealed class UnreachableKeyProtector : IConfigProtector
    {
        public byte[] Protect(byte[] data) => throw new ConfigKeyUnavailableException("locked");
        public byte[] Unprotect(byte[] data) => throw new ConfigKeyUnavailableException("locked");
    }
}

/// <summary>
/// The real per-user encryption of the current OS: DPAPI on Windows, AES with a keychain key on
/// macOS. The macOS tests use a throwaway keychain so the login keychain stays untouched.
/// </summary>
public class PlatformConfigProtectorTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "SshTunnelerTests", Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_directory, "config.dat");

    private string KeychainPath => Path.Combine(_directory, "test.keychain-db");

    private bool _keychainCreated;

    public PlatformConfigProtectorTests() => Directory.CreateDirectory(_directory);

    /// <summary>A fresh protector each time, as after an app restart; null where unsupported.</summary>
    private IConfigProtector? NewProtector()
    {
        if (OperatingSystem.IsWindows()) return new DpapiConfigProtector();
        if (!OperatingSystem.IsMacOS()) return null;

        if (!_keychainCreated)
        {
            Security("create-keychain", "-p", "test", KeychainPath);
            Security("unlock-keychain", "-p", "test", KeychainPath);
            _keychainCreated = true;
        }

        return new KeychainConfigProtector(KeychainPath);
    }

    [Fact]
    public void SaveThenLoad_WithAFreshProtector_RoundTripsSecrets()
    {
        if (NewProtector() is not { } protector) return;

        var config = new AppConfig();
        config.Servers.Add(new SshServer { Name = "prod", Host = "10.0.0.5", Secret = "s3cret" });
        new EncryptedJsonConfigStore(protector, ConfigPath).Save(config);

        var loaded = new EncryptedJsonConfigStore(NewProtector()!, ConfigPath).Load();

        Assert.Equal("s3cret", loaded.Servers.Single().Secret);
    }

    [Fact]
    public void SavedFile_IsEncryptedNotPlainJson()
    {
        if (NewProtector() is not { } protector) return;

        var config = new AppConfig();
        config.Servers.Add(new SshServer { Name = "prod", Host = "10.0.0.5", Secret = "s3cret" });
        new EncryptedJsonConfigStore(protector, ConfigPath).Save(config);

        var asText = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(ConfigPath));
        Assert.DoesNotContain("s3cret", asText);
        Assert.DoesNotContain("10.0.0.5", asText);
    }

    [Fact]
    public void Unprotect_WithoutAStoredKey_FailsAsUndecryptable()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var protector = NewProtector()!;

        // No key yet: the file can never be decrypted, so the store must treat it as corrupt,
        // and reading must not invent a new key that would hide the problem.
        Assert.Throws<CryptographicException>(() => protector.Unprotect(new byte[64]));
    }

    private static void Security(params string[] args)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/security")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"security {args[0]}: {error}");
    }

    public void Dispose()
    {
        try
        {
            if (_keychainCreated) Security("delete-keychain", KeychainPath);
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (Exception)
        {
            // Temp cleanup is best effort.
        }
    }
}
