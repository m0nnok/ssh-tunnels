using System.Threading;
using SshTunneler.Services;

namespace SshTunneler;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // ssh.exe launches SSH_ASKPASS with the prompt as the only argument, so the helper mode
        // is recognised by the environment variables the app sets on the ssh child process.
        var askPassRequested = args.Contains(AskPassProtocol.Switch) ||
                               Environment.GetEnvironmentVariable(AskPassProtocol.TokenEnvVar) is not null;

        if (askPassRequested)
        {
            var promptArgs = args.Where(a => a != AskPassProtocol.Switch).ToArray();
            return AskPassClient.Run([AskPassProtocol.Switch, promptArgs.FirstOrDefault() ?? "Password:"]);
        }

        using var singleInstance = new Mutex(true, @"Local\SshTunneler.SingleInstance", out var isOwner);
        if (!isOwner) return 0;

        var app = new App();
        return app.Run();
    }
}
