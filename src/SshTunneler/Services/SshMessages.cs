namespace SshTunneler.Services;

/// <summary>Turns raw ssh diagnostics into something readable in a small status line.</summary>
public static class SshMessages
{
    public static string Humanize(string sshLine)
    {
        var line = sshLine.Trim();

        if (line.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
            return "Аутентификация отклонена (проверьте ключ, пароль или логин)";

        if (line.Contains("Address already in use", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("cannot listen to port", StringComparison.OrdinalIgnoreCase))
            return "Локальный порт уже занят другим процессом";

        if (line.Contains("Connection refused", StringComparison.OrdinalIgnoreCase))
            return "Соединение отклонено (сервер недоступен или порт закрыт)";

        if (line.Contains("Connection timed out", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Operation timed out", StringComparison.OrdinalIgnoreCase))
            return "Таймаут подключения к серверу";

        if (line.Contains("Could not resolve hostname", StringComparison.OrdinalIgnoreCase))
            return "Не удалось разрешить имя хоста";

        if (line.Contains("REMOTE HOST IDENTIFICATION HAS CHANGED", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Host key verification failed", StringComparison.OrdinalIgnoreCase))
            return "Ключ хоста изменился или не подтверждён (проверьте known_hosts)";

        if (line.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("identity file", StringComparison.OrdinalIgnoreCase))
            return "Файл приватного ключа не найден";

        if (line.Contains("Too many authentication failures", StringComparison.OrdinalIgnoreCase))
            return "Слишком много неудачных попыток аутентификации";

        if (line.Contains("Connection closed", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Connection reset", StringComparison.OrdinalIgnoreCase))
            return "Сервер закрыл соединение";

        return line;
    }
}
