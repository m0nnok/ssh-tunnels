using System.Diagnostics;
using System.Text;
using SshTunneler.Models;

namespace SshTunneler.Services;

public sealed record ConnectionTestResult(bool Success, string Message);

/// <summary>Runs a short ssh session to verify an access works before saving it.</summary>
public static class ConnectionTester
{
    public static async Task<ConnectionTestResult> TestAsync(
        SshServer server, AskPassServer askPass, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(server.Host))
            return new ConnectionTestResult(false, "Не заполнен адрес сервера");

        var token = askPass.Register(_ => server.Secret);
        try
        {
            var startInfo = new ProcessStartInfo(SshArgumentBuilder.ResolveSshPath())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            foreach (var arg in SshArgumentBuilder.BuildTestArgs(server)) startInfo.ArgumentList.Add(arg);
            startInfo.Environment["SSH_ASKPASS"] = Environment.ProcessPath ?? "";
            startInfo.Environment["SSH_ASKPASS_REQUIRE"] = "force";
            startInfo.Environment["DISPLAY"] = "localhost:0";
            startInfo.Environment[AskPassProtocol.PipeEnvVar] = askPass.PipeName;
            startInfo.Environment[AskPassProtocol.TokenEnvVar] = token;
            startInfo.Environment[AskPassProtocol.LabelEnvVar] = server.ToString();

            using var process = Process.Start(startInfo);
            if (process is null) return new ConnectionTestResult(false, "Не удалось запустить ssh.exe");

            var stderr = new StringBuilder();
            var readTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync()) is not null)
                    if (!line.StartsWith("debug", StringComparison.OrdinalIgnoreCase))
                        stderr.AppendLine(line);
            }, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { }
                return new ConnectionTestResult(false, "Таймаут: сервер не ответил за 30 секунд");
            }

            await Task.WhenAny(readTask, Task.Delay(500, CancellationToken.None));

            if (process.ExitCode == 0)
                return new ConnectionTestResult(true, "Подключение успешно");

            var lastLine = stderr.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();

            return new ConnectionTestResult(false, lastLine is null
                ? $"ssh завершился с кодом {process.ExitCode}"
                : SshMessages.Humanize(lastLine));
        }
        finally
        {
            askPass.Unregister(token);
        }
    }
}
