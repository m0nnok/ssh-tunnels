namespace SshTunneler.Views;

/// <summary>Labels that name OS specifics: the tray is the menu bar on macOS, and so on.</summary>
public static class PlatformText
{
    public static string HideTip => OperatingSystem.IsMacOS()
        ? "Скрыть в строку меню (выход — из меню в строке меню)"
        : "Свернуть в трей (выход — из меню в трее)";

    public static string AutoStart => OperatingSystem.IsWindows()
        ? "Запускать вместе с Windows"
        : "Запускать при входе в систему";

    public static string StartMinimized => OperatingSystem.IsMacOS()
        ? "Стартовать скрытым в строке меню"
        : "Стартовать свёрнутым в трей";

    public static string Browser => OperatingSystem.IsMacOS()
        ? "Браузер: имя приложения или путь к .app (пусто — системный по умолчанию)"
        : "Браузер (пусто — системный по умолчанию)";
}
