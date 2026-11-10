using System.Globalization;
using Avalonia.Data.Converters;
using SshTunneler.Models;

namespace SshTunneler.Views;

/// <summary>Shows authentication methods in the settings combo box in plain Russian.</summary>
public sealed class AuthMethodNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is AuthMethod method
            ? method switch
            {
                AuthMethod.Agent => "ssh-agent / по умолчанию",
                AuthMethod.KeyFile => "Приватный ключ",
                AuthMethod.Password => "Пароль",
                _ => value.ToString() ?? ""
            }
            : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
