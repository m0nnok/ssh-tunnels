using SshTunneler.Models;
using SshTunneler.Services;
using Xunit;

namespace SshTunneler.Tests;

public class SshArgumentBuilderTests
{
    private static SshServer Server(AuthMethod method = AuthMethod.Agent) => new()
    {
        Name = "test",
        Host = "example.com",
        User = "deploy",
        Port = 22,
        AuthMethod = method
    };

    private static TunnelResource Resource() => new()
    {
        Name = "grafana",
        RemoteHost = "127.0.0.1",
        RemotePort = 3000
    };

    [Fact]
    public void TunnelArgs_ForwardLocalPortToRemoteResource()
    {
        var args = SshArgumentBuilder.BuildTunnelArgs(Server(), Resource(), 20001);

        Assert.Contains("-N", args);
        Assert.Contains("-L", args);
        Assert.Contains("127.0.0.1:20001:127.0.0.1:3000", args);
        Assert.Equal("deploy@example.com", args[^1]);
    }

    [Fact]
    public void TunnelArgs_AlwaysFailFastOnForwardProblems()
    {
        var args = SshArgumentBuilder.BuildTunnelArgs(Server(), Resource(), 20001);

        Assert.Contains("ExitOnForwardFailure=yes", args);
        Assert.Contains("ServerAliveInterval=15", args);
        Assert.Contains("StrictHostKeyChecking=accept-new", args);
    }

    [Fact]
    public void AgentAuth_UsesBatchModeSoItNeverBlocksOnAPrompt()
    {
        var args = SshArgumentBuilder.BuildTunnelArgs(Server(), Resource(), 20001);

        Assert.Contains("BatchMode=yes", args);
    }

    [Fact]
    public void KeyFileAuth_PassesTheKeyAndPinsPublicKeyAuth()
    {
        var server = Server(AuthMethod.KeyFile);
        server.KeyPath = @"C:\keys\id_ed25519";

        var args = SshArgumentBuilder.BuildTunnelArgs(server, Resource(), 20001);

        Assert.Contains("-i", args);
        Assert.Contains(@"C:\keys\id_ed25519", args);
        Assert.Contains("IdentitiesOnly=yes", args);
        Assert.Contains("PreferredAuthentications=publickey", args);
        Assert.DoesNotContain("BatchMode=yes", args);
    }

    [Fact]
    public void PasswordAuth_DisablesPublicKeysSoTheAskpassPromptIsReached()
    {
        var args = SshArgumentBuilder.BuildTunnelArgs(Server(AuthMethod.Password), Resource(), 20001);

        Assert.Contains("PubkeyAuthentication=no", args);
        Assert.Contains("PreferredAuthentications=password,keyboard-interactive", args);
        Assert.DoesNotContain("BatchMode=yes", args);
    }

    [Fact]
    public void CustomPort_IsPassedOnlyWhenItIsNotTheDefault()
    {
        Assert.DoesNotContain("-p", SshArgumentBuilder.BuildTunnelArgs(Server(), Resource(), 20001));

        var server = Server();
        server.Port = 2222;
        var args = SshArgumentBuilder.BuildTunnelArgs(server, Resource(), 20001);

        Assert.Contains("-p", args);
        Assert.Contains("2222", args);
    }

    [Fact]
    public void EmptyUser_LeavesTheTargetToSshConfig()
    {
        var server = Server();
        server.User = "";

        var args = SshArgumentBuilder.BuildTunnelArgs(server, Resource(), 20001);

        Assert.Equal("example.com", args[^1]);
    }

    [Fact]
    public void ExtraOptions_AreSplitOnWhitespaceAndRespectQuotes()
    {
        var parts = SshArgumentBuilder.SplitExtraOptions("-o ProxyJump=bastion -o \"ProxyCommand=nc -x 1080 %h %p\"").ToList();

        Assert.Equal(["-o", "ProxyJump=bastion", "-o", "ProxyCommand=nc -x 1080 %h %p"], parts);
    }

    [Fact]
    public void TestArgs_RunAnImmediateExitWithATimeout()
    {
        var args = SshArgumentBuilder.BuildTestArgs(Server());

        Assert.Contains("ConnectTimeout=10", args);
        Assert.Equal("exit", args[^1]);
        Assert.Equal("deploy@example.com", args[^2]);
    }
}
