using System.Diagnostics;
using System.IO;
using SshTunneler.Services;
using Xunit;

namespace SshTunneler.Tests;

/// <summary>
/// Covers the askpass chain end to end: the app hands a secret to its own --askpass mode
/// over a named pipe, and the system OpenSSH really does call SSH_ASKPASS the way we rely on.
/// </summary>
public class AskPassIntegrationTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "SshTunnelerTests", Guid.NewGuid().ToString("N"));

    private static string AppExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "SshTunneler.exe" : "SshTunneler");

    [Fact]
    public void AskPassMode_PrintsTheSecretHandedOverThePipe()
    {
        using var server = new AskPassServer();
        var token = server.Register(_ => "hunter2");

        var result = Run(AppExe, [AskPassProtocol.Switch, "deploy@host password:"], new Dictionary<string, string>
        {
            [AskPassProtocol.PipeEnvVar] = server.PipeName,
            [AskPassProtocol.TokenEnvVar] = token,
            [AskPassClient.NoDialogEnvVar] = "1"
        });

        Assert.True(result.ExitCode == 0, $"exit={result.ExitCode} stderr={result.StandardError}");
        Assert.Equal("hunter2", result.StandardOutput.Trim());
    }

    [Fact]
    public void AskPassMode_IsAlsoSelectedByEnvironmentAlone()
    {
        // ssh passes only the prompt, so the helper must recognise itself by the environment.
        using var server = new AskPassServer();
        var token = server.Register(_ => "from-env");

        var result = Run(AppExe, ["Enter passphrase for key:"], new Dictionary<string, string>
        {
            [AskPassProtocol.PipeEnvVar] = server.PipeName,
            [AskPassProtocol.TokenEnvVar] = token,
            [AskPassClient.NoDialogEnvVar] = "1"
        });

        Assert.True(result.ExitCode == 0, $"exit={result.ExitCode} stderr={result.StandardError}");
        Assert.Equal("from-env", result.StandardOutput.Trim());
    }

    [Fact]
    public void OpenSshReallyCallsTheHelper_WhenItNeedsAKeyPassphrase()
    {
        var sshKeygen = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh-keygen.exe")
            : "/usr/bin/ssh-keygen";
        if (!File.Exists(sshKeygen)) return;

        Directory.CreateDirectory(_directory);
        var keyPath = Path.Combine(_directory, "id_test");

        var generated = Run(sshKeygen, ["-q", "-t", "ed25519", "-N", "pass-phrase-42", "-C", "test", "-f", keyPath],
            new Dictionary<string, string>());
        Assert.True(generated.ExitCode == 0, generated.StandardError);

        using var server = new AskPassServer();
        var token = server.Register(_ => "pass-phrase-42");

        // The same environment the app gives ssh, pointed at the app instead of the test host.
        var environment = AskPassProtocol.BuildSshEnvironment(server.PipeName, token, "test");
        environment["SSH_ASKPASS"] = AppExe;
        environment[AskPassClient.NoDialogEnvVar] = "1";

        // ssh-keygen -y needs the passphrase and, with SSH_ASKPASS_REQUIRE=force, must ask the helper.
        var result = Run(sshKeygen, ["-y", "-f", keyPath], environment);

        Assert.True(result.ExitCode == 0, $"exit={result.ExitCode} stderr={result.StandardError}");
        Assert.StartsWith("ssh-ed25519", result.StandardOutput.Trim());
    }

    private static (int ExitCode, string StandardOutput, string StandardError) Run(
        string exe, string[] args, Dictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        foreach (var (key, value) in environment) startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{Path.GetFileName(exe)} did not exit, the helper probably fell back to a dialog");
        }

        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
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
