using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SshTunneler.Services;
using Xunit;

namespace SshTunneler.Tests;

public class AskPassServerTests
{
    [Fact]
    public void RegisteredToken_GetsTheSecret()
    {
        using var server = new AskPassServer();
        var token = server.Register(prompt => prompt.Contains("passphrase") ? "phrase" : "password");

        var response = Ask(server.PipeName, token, "Enter passphrase for key:");

        Assert.True(response.Found);
        Assert.Equal("phrase", response.Secret);
    }

    [Fact]
    public void UnknownToken_GetsNothing()
    {
        using var server = new AskPassServer();
        server.Register(_ => "secret");

        var response = Ask(server.PipeName, "0000", "password:");

        Assert.False(response.Found);
        Assert.Null(response.Secret);
    }

    [Fact]
    public void UnregisteredToken_StopsWorking()
    {
        using var server = new AskPassServer();
        var token = server.Register(_ => "secret");
        server.Unregister(token);

        Assert.False(Ask(server.PipeName, token, "password:").Found);
    }

    private static AskPassProtocol.Response Ask(string pipeName, string token, string prompt)
    {
        using var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Impersonation);
        pipe.Connect(5000);

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);

        writer.WriteLine(JsonSerializer.Serialize(new AskPassProtocol.Request(token, prompt)));
        var line = reader.ReadLine();

        Assert.False(string.IsNullOrEmpty(line), "сервер не ответил");
        return JsonSerializer.Deserialize<AskPassProtocol.Response>(line!)!;
    }
}
