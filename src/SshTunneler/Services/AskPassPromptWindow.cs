using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace SshTunneler.Services;

/// <summary>
/// Fallback prompt shown by the askpass helper when the app has no stored secret,
/// which also covers one-time codes and host key confirmations.
/// </summary>
internal sealed class AskPassPromptWindow : Window
{
    private readonly TextBox _input;

    private AskPassPromptWindow(string prompt, string? label)
    {
        Title = string.IsNullOrWhiteSpace(label) ? "SSH Tunneler" : $"SSH Tunneler — {label}";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var isYesNo = prompt.Contains("yes/no", StringComparison.OrdinalIgnoreCase);
        _input = new TextBox { PasswordChar = isYesNo ? default : '●', Margin = new Thickness(0, 0, 0, 10) };

        var ok = new Button { Content = "OK", IsDefault = true, Classes = { "dialog" } };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Classes = { "dialog" } };
        ok.Click += (_, _) => { Result = _input.Text ?? ""; Close(); };
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(14, 12),
            Children =
            {
                new TextBlock { Text = prompt, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) },
                _input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { ok, cancel }
                }
            }
        };

        // The helper runs as a background app, so the window has to pull itself to the front.
        Opened += (_, _) => { Activate(); _input.Focus(); };
    }

    /// <summary>What the user typed, or null when the prompt was cancelled or closed.</summary>
    public string? Result { get; private set; }

    public static string? Ask(string prompt, string? label)
    {
        var app = new PromptApp(prompt, label);
        Program.ConfigurePlatform(AppBuilder.Configure(() => app))
            .StartWithClassicDesktopLifetime([], ShutdownMode.OnMainWindowClose);
        return app.Result;
    }

    /// <summary>A bare application for the helper process: theme plus the single prompt window.</summary>
    private sealed class PromptApp(string prompt, string? label) : Application
    {
        public string? Result { get; private set; }

        public override void Initialize()
        {
            RequestedThemeVariant = ThemeVariant.Dark;
            Styles.Add(new FluentTheme());
            Styles.Add(new StyleInclude(new Uri("avares://SshTunneler/"))
            {
                Source = new Uri("avares://SshTunneler/Themes/Dark.axaml")
            });
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = new AskPassPromptWindow(prompt, label);
                window.Closed += (_, _) => Result = window.Result;
                desktop.MainWindow = window;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
