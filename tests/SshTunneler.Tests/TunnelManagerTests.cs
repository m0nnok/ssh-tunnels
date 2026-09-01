using System.Net;
using System.Net.Sockets;
using SshTunneler.Models;
using SshTunneler.Services;
using Xunit;

namespace SshTunneler.Tests;

/// <summary>Drives the real ssh.exe against dead endpoints to check the failure paths.</summary>
public class TunnelManagerTests
{
    private static (AppConfig Config, SshServer Server, TunnelResource Resource) Scenario(int localPort = 0)
    {
        var config = new AppConfig();
        config.Ui.AutoReconnect = false;

        var server = new SshServer
        {
            Name = "dead",
            Host = "127.0.0.1",
            Port = 47654, // Nothing listens here.
            User = "nobody",
            AuthMethod = AuthMethod.Agent
        };

        var resource = new TunnelResource
        {
            Name = "resource",
            ServerId = server.Id,
            RemoteHost = "127.0.0.1",
            RemotePort = 3000,
            LocalPort = localPort
        };

        config.Servers.Add(server);
        config.Resources.Add(resource);
        return (config, server, resource);
    }

    [Fact]
    public async Task Connect_ToADeadServer_FailsWithAReadableReason()
    {
        var (config, _, resource) = Scenario();
        using var askPass = new AskPassServer();
        using var tunnels = new TunnelManager(config, askPass);

        var connected = await tunnels.ConnectAsync(resource.Id);
        var status = tunnels.GetStatus(resource.Id);

        Assert.False(connected);
        Assert.Equal(TunnelState.Failed, status.State);
        Assert.False(string.IsNullOrWhiteSpace(status.Message));
        Assert.DoesNotContain("debug", status.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(tunnels.GetLog(resource.Id));
    }

    [Fact]
    public async Task Connect_WithAnOccupiedLocalPort_FailsBeforeStartingSsh()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var busyPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var (config, _, resource) = Scenario(busyPort);
            using var askPass = new AskPassServer();
            using var tunnels = new TunnelManager(config, askPass);

            var connected = await tunnels.ConnectAsync(resource.Id);

            Assert.False(connected);
            Assert.Contains(busyPort.ToString(), tunnels.GetStatus(resource.Id).Message);
            Assert.Empty(tunnels.GetLog(resource.Id));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Connect_WithoutAServer_SaysSoInsteadOfLaunchingSsh()
    {
        var config = new AppConfig();
        config.Ui.AutoReconnect = false;
        var resource = new TunnelResource { Name = "orphan", ServerId = Guid.NewGuid() };
        config.Resources.Add(resource);

        using var askPass = new AskPassServer();
        using var tunnels = new TunnelManager(config, askPass);

        Assert.False(await tunnels.ConnectAsync(resource.Id));
        Assert.Equal("Для ресурса не выбран сервер", tunnels.GetStatus(resource.Id).Message);
    }

    [Fact]
    public async Task Disconnect_ReturnsTheTunnelToIdle()
    {
        var (config, _, resource) = Scenario();
        using var askPass = new AskPassServer();
        using var tunnels = new TunnelManager(config, askPass);

        await tunnels.ConnectAsync(resource.Id);
        await tunnels.DisconnectAsync(resource.Id);

        Assert.Equal(TunnelState.Idle, tunnels.GetStatus(resource.Id).State);
        Assert.Empty(tunnels.ActiveResourceIds);
    }
}
