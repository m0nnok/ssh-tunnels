using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace SshTunneler.Services;

/// <summary>The --askpass side of the app: fetch the secret and print it to stdout for ssh.exe.</summary>
public static class AskPassClient
{
    /// <summary>Set to 1 to fail instead of showing the interactive fallback, used by tests.</summary>
    public const string NoDialogEnvVar = "SSHTUNNELER_ASKPASS_NODIALOG";

    public static int Run(string[] args)
    {
        var prompt = args.Length > 1 ? args[1] : "Password:";
        var label = Environment.GetEnvironmentVariable(AskPassProtocol.LabelEnvVar);

        var secret = TryFetchFromApp(prompt, out var error);
        if (secret is null)
        {
            if (Environment.GetEnvironmentVariable(NoDialogEnvVar) == "1")
            {
                Console.Error.WriteLine(error ?? "секрет не найден");
                return 2;
            }

            secret = AskPassPromptWindow.Ask(prompt, label);
        }

        if (secret is null) return 1;

        // stdout here is the pipe ssh.exe created for the helper.
        using var stdout = Console.OpenStandardOutput();
        var bytes = Encoding.UTF8.GetBytes(secret + "\n");
        stdout.Write(bytes, 0, bytes.Length);
        stdout.Flush();
        return 0;
    }

    private static string? TryFetchFromApp(string prompt, out string? error)
    {
        error = null;
        var pipeName = Environment.GetEnvironmentVariable(AskPassProtocol.PipeEnvVar);
        var token = Environment.GetEnvironmentVariable(AskPassProtocol.TokenEnvVar);
        if (string.IsNullOrEmpty(pipeName) || string.IsNullOrEmpty(token))
        {
            error = "нет данных о канале с приложением";
            return null;
        }

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Impersonation);
            pipe.Connect(3000);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);

            writer.WriteLine(JsonSerializer.Serialize(new AskPassProtocol.Request(token, prompt)));
            var line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
            {
                error = "приложение не ответило";
                return null;
            }

            var response = JsonSerializer.Deserialize<AskPassProtocol.Response>(line);
            if (response is { Found: true } && !string.IsNullOrEmpty(response.Secret)) return response.Secret;

            error = "секрет для этого подключения не сохранён";
            return null;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }
}
