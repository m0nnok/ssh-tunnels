using System.Globalization;
using System.Windows.Data;
using SshTunneler.Models;

namespace SshTunneler.Views;

/// <summary>Shows authentication methods in the settings combo box in plain Russian.</summary>
public sealed class AuthMethodNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is AuthMethod method
            ? method switch
            {
                AuthMethod.Agent => "ssh-agent / по умолчанию",
                AuthMethod.KeyFile => "Приватный ключ",
                AuthMethod.Password => "Пароль",
                _ => value.ToString() ?? ""
            }
            : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Enables the editor pane only when something is selected in the list.</summary>
public sealed class NullToBoolConverter : IValueConverter
{
    public static readonly NullToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
