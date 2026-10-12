using System.IO;
using SshTunneler.Models;
using SshTunneler.Services;
using Xunit;

namespace SshTunneler.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "SshTunnelerTests", Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_directory, "config.dat");

    [Fact]
    public void SaveThenLoad_RoundTripsServersResourcesAndSecrets()
    {
        var store = new DpapiJsonConfigStore(ConfigPath);
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

        store.Save(config);
        var loaded = new DpapiJsonConfigStore(ConfigPath).Load();

        Assert.Equal("prod", loaded.Servers.Single().Name);
        Assert.Equal("s3cret", loaded.Servers.Single().Secret);
        Assert.Equal(server.Id, loaded.Resources.Single().ServerId);
        Assert.Equal(3000, loaded.Resources.Single().RemotePort);
        Assert.Equal(0.5, loaded.Ui.Opacity);
    }

    [Fact]
    public void SavedFile_IsEncryptedNotPlainJson()
    {
        var store = new DpapiJsonConfigStore(ConfigPath);
        var config = new AppConfig();
        config.Servers.Add(new SshServer { Name = "prod", Host = "10.0.0.5", Secret = "s3cret" });

        store.Save(config);

        var bytes = File.ReadAllBytes(ConfigPath);
        var asText = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("s3cret", asText);
        Assert.DoesNotContain("10.0.0.5", asText);
    }

    [Fact]
    public void Load_OnMissingFile_ReturnsEmptyConfiguration()
    {
        var loaded = new DpapiJsonConfigStore(ConfigPath).Load();

        Assert.Empty(loaded.Servers);
        Assert.Empty(loaded.Resources);
    }

    [Fact]
    public void Load_OnUnreadableFile_ThrowsAndQuarantinesIt()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath, "this is not a DPAPI blob");

        var store = new DpapiJsonConfigStore(ConfigPath);
        var ex = Assert.Throws<ConfigLoadException>(() => store.Load());

        Assert.NotNull(ex.QuarantinedPath);
        Assert.True(File.Exists(ex.QuarantinedPath!));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public void SecondSave_KeepsThePreviousVersionAsBackup()
    {
        var store = new DpapiJsonConfigStore(ConfigPath);
        var config = new AppConfig();
        store.Save(config);

        config.Servers.Add(new SshServer { Name = "prod", Host = "10.0.0.5" });
        store.Save(config);

        Assert.True(File.Exists(ConfigPath + ".bak"));
        Assert.False(File.Exists(ConfigPath + ".tmp"));
        Assert.Single(new DpapiJsonConfigStore(ConfigPath).Load().Servers);
    }

    [Fact]
    public void Load_WhenMainFileIsMissing_FallsBackToThePreviousSave()
    {
        var store = new DpapiJsonConfigStore(ConfigPath);
        var config = new AppConfig();
        config.Servers.Add(new SshServer { Name = "prod", Host = "10.0.0.5" });
        store.Save(config);
        config.Servers.Add(new SshServer { Name = "staging", Host = "10.0.0.6" });
        store.Save(config);

        File.Delete(ConfigPath);

        // The backup trails the current file by one save, so this is the state before "staging".
        Assert.Equal("prod", new DpapiJsonConfigStore(ConfigPath).Load().Servers.Single().Name);
    }

    [Fact]
    public void Load_WhenMainFileIsUnreadable_RecoversFromBackupAndQuarantinesIt()
    {
        var store = new DpapiJsonConfigStore(ConfigPath);
        var config = new AppConfig();
        config.Servers.Add(new SshServer { Name = "prod", Host = "10.0.0.5" });
        store.Save(config);
        store.Save(config); // second save produces the .bak copy

        File.WriteAllText(ConfigPath, "this is not a DPAPI blob");

        Assert.Equal("prod", new DpapiJsonConfigStore(ConfigPath).Load().Servers.Single().Name);
        Assert.False(File.Exists(ConfigPath));
        Assert.Single(Directory.GetFiles(_directory, "config.dat.corrupt-*"));
    }

    [Fact]
    public void Constructor_RejectsARelativePath()
    {
        // A relative path resolves against the working directory, which for anything started from
        // the Run key is C:\Windows\System32 — the saved settings would look wiped.
        Assert.Throws<ArgumentException>(() => new DpapiJsonConfigStore(@"SshTunneler\config.dat"));
    }

    [Fact]
    public void DefaultPath_IsAbsolute()
    {
        Assert.True(Path.IsPathFullyQualified(DpapiJsonConfigStore.RoamingAppDataDirectory()));
        Assert.True(Path.IsPathFullyQualified(DpapiJsonConfigStore.DefaultPath));
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
}
