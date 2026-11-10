using System.Threading;
using Avalonia;
using Avalonia.Controls;
using SshTunneler.Services;

namespace SshTunneler;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // ssh launches SSH_ASKPASS with the prompt as the only argument, so the helper mode
        // is recognised by the environment variables the app sets on the ssh child process.
        var askPassRequested = args.Contains(AskPassProtocol.Switch) ||
                               Environment.GetEnvironmentVariable(AskPassProtocol.TokenEnvVar) is not null;

        if (askPassRequested)
        {
            var promptArgs = args.Where(a => a != AskPassProtocol.Switch).ToArray();
            return AskPassClient.Run([AskPassProtocol.Switch, promptArgs.FirstOrDefault() ?? "Password:"]);
        }

        // On Unix a session-scoped mutex would not be shared between a Finder launch and a
        // Terminal launch, so there the guard is per user; on Windows it stays per session.
        using var singleInstance = new Mutex(
            true,
            "SshTunneler.SingleInstance",
            new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = OperatingSystem.IsWindows() },
            out var isOwner);
        if (!isOwner) return 0;

        // Closing the window only hides it to the tray; the app ends from the tray menu.
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>Also used by the Avalonia previewer, hence public.</summary>
    public static AppBuilder BuildAvaloniaApp() => ConfigurePlatform(AppBuilder.Configure<App>());

    /// <summary>Platform setup shared by the app and the askpass prompt.</summary>
    internal static AppBuilder ConfigurePlatform(AppBuilder builder) => builder
        .UsePlatformDetect()
        // A menu bar app: no Dock icon, also when the executable runs outside an .app bundle.
        .With(new MacOSPlatformOptions { ShowInDock = false })
        .LogToTrace();
}
