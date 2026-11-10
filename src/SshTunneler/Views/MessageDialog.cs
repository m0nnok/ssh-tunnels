using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SshTunneler.Views;

/// <summary>A message box in the app's own style: OK, or Yes/No with No as the safe default.</summary>
public sealed class MessageDialog : Window
{
    private bool _confirmed;

    private MessageDialog(string message, bool yesNo)
    {
        Title = "SSH Tunneler";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (yesNo)
        {
            var yes = new Button { Content = "Да", Classes = { "dialog" } };
            yes.Click += (_, _) => { _confirmed = true; Close(); };
            var no = new Button { Content = "Нет", IsDefault = true, IsCancel = true, Classes = { "dialog" } };
            no.Click += (_, _) => Close();
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
        }
        else
        {
            var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, Classes = { "dialog" } };
            ok.Click += (_, _) => { _confirmed = true; Close(); };
            buttons.Children.Add(ok);
        }

        Content = new StackPanel
        {
            Margin = new Thickness(16, 14),
            Children =
            {
                // Selectable, so a file path in the message can be copied.
                new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) },
                buttons
            }
        };
    }

    public static Task ShowAsync(Window? owner, string message)
        => new MessageDialog(message, yesNo: false).ShowModalAsync(owner);

    public static async Task<bool> AskYesNoAsync(Window? owner, string message)
    {
        var dialog = new MessageDialog(message, yesNo: true);
        await dialog.ShowModalAsync(owner);
        return dialog._confirmed;
    }
}
