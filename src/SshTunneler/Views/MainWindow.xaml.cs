using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SshTunneler.ViewModels;

namespace SshTunneler.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        RestoreGeometry();

        viewModel.SettingsRequested += OpenSettings;
        viewModel.HideRequested += HideToTray;
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void HideToTray()
    {
        SaveGeometry();
        Hide();
    }

    /// <summary>Persists window placement and opacity, called before the app goes away.</summary>
    public void SaveGeometry()
    {
        var ui = _viewModel.Config.Ui;
        if (WindowState == WindowState.Normal)
        {
            ui.Left = Left;
            ui.Top = Top;
            ui.Width = Width;
            ui.Height = Height;
        }

        try
        {
            _viewModel.Save();
        }
        catch (Exception)
        {
            // Saving geometry must never block closing the window.
        }
    }

    private void RestoreGeometry()
    {
        var ui = _viewModel.Config.Ui;
        Width = ui.Width > 0 ? ui.Width : 380;
        Height = ui.Height > 0 ? ui.Height : 280;

        if (ui.Left is not { } savedLeft || ui.Top is not { } savedTop)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        // Keep the window reachable if the monitor layout changed since last run.
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        Left = Math.Clamp(savedLeft, virtualLeft, Math.Max(virtualLeft, virtualRight - 100));
        Top = Math.Clamp(savedTop, virtualTop, Math.Max(virtualTop, virtualBottom - 60));
    }

    private void OpenSettings(Guid? focusResourceId)
    {
        var app = (App)Application.Current;
        var settings = new SettingsWindow(new SettingsViewModel(_viewModel.Config, app.AskPass), focusResourceId)
        {
            Owner = IsVisible ? this : null
        };

        if (settings.ShowDialog() == true && settings.Result is not null)
            _viewModel.ApplyConfig(settings.Result);
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2) return;
        DragMove();
    }

    private void OnWindowMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        var ui = _viewModel.Config.Ui;
        ui.Opacity = Math.Clamp(ui.Opacity + (e.Delta > 0 ? 0.05 : -0.05), 0.35, 1.0);
        Opacity = ui.Opacity;
        e.Handled = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var app = (App)Application.Current;
        if (!app.IsShuttingDown)
        {
            // The close button hides to tray, only the tray menu really exits.
            e.Cancel = true;
            HideToTray();
            return;
        }

        SaveGeometry();
        base.OnClosing(e);
    }
}
