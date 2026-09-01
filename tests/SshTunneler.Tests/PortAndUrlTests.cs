using System.Net;
using System.Net.Sockets;
using SshTunneler.Models;
using SshTunneler.Services;
using Xunit;

namespace SshTunneler.Tests;

public class PortAllocatorTests
{
    [Fact]
    public void IsPortFree_IsFalseWhileSomethingListens()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.False(PortAllocator.IsPortFree(port));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Allocate_SkipsPortsAlreadyTakenByOtherTunnels()
    {
        var first = PortAllocator.Allocate(20000, 20050);
        var second = PortAllocator.Allocate(20000, 20050, [first]);

        Assert.InRange(first, 20000, 20050);
        Assert.InRange(second, 20000, 20050);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Allocate_ReturnsZeroWhenTheRangeIsExhausted()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.Equal(0, PortAllocator.Allocate(port, port));
        }
        finally
        {
            listener.Stop();
        }
    }
}

public class BrowserLauncherTests
{
    [Fact]
    public void BuildUrl_SubstitutesEveryPlaceholder()
    {
        var resource = new TunnelResource
        {
            RemoteHost = "10.1.2.3",
            RemotePort = 8080,
            UrlTemplate = "http://127.0.0.1:{localPort}/from/{remoteHost}/{remotePort}"
        };

        Assert.Equal("http://127.0.0.1:20005/from/10.1.2.3/8080", BrowserLauncher.BuildUrl(resource, 20005));
    }

    [Fact]
    public void BuildUrl_FallsBackToLoopbackWhenTemplateIsEmpty()
    {
        var resource = new TunnelResource { UrlTemplate = "" };

        Assert.Equal("http://127.0.0.1:20005", BrowserLauncher.BuildUrl(resource, 20005));
    }
}

public class SshMessagesTests
{
    [Theory]
    [InlineData("deploy@host: Permission denied (publickey).", "Аутентификация отклонена")]
    [InlineData("bind [127.0.0.1]:20001: Address already in use", "Локальный порт уже занят")]
    [InlineData("ssh: connect to host x port 22: Connection refused", "Соединение отклонено")]
    public void Humanize_ExplainsCommonSshFailures(string sshLine, string expectedPrefix)
    {
        Assert.StartsWith(expectedPrefix, SshMessages.Humanize(sshLine));
    }

    [Fact]
    public void Humanize_KeepsUnknownLinesAsIs()
    {
        Assert.Equal("something entirely new", SshMessages.Humanize("  something entirely new  "));
    }
}
