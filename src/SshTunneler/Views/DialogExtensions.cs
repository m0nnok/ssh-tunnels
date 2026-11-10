using Avalonia.Controls;

namespace SshTunneler.Views;

public static class DialogExtensions
{
    /// <summary>
    /// Shows the window modally over the owner when the owner is on screen. ShowDialog refuses a
    /// hidden owner, and the main window often sits hidden in the tray, so then the window opens
    /// on its own; either way the caller resumes once it is closed.
    /// </summary>
    public static async Task ShowModalAsync(this Window window, Window? owner)
    {
        if (owner is { IsVisible: true })
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            await window.ShowDialog(owner);
            return;
        }

        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
        window.Activate();
        await closed.Task;
    }
}
