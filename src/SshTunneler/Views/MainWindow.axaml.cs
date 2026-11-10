using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SshTunneler.ViewModels;

namespace SshTunneler.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private SettingsWindow? _settings;
    private bool _hasBeenShown;
    private Point? _resizeStart;
    private Size _resizeStartSize;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        RestoreGeometry();

        viewModel.SettingsRequested += OpenSettings;
        viewModel.HideRequested += HideToTray;
        Opened += (_, _) => _hasBeenShown = true;

        // Tunnel routing: the scroll viewer would otherwise take the wheel before the window sees it.
        AddHandler(PointerWheelChangedEvent, OnWindowWheel, RoutingStrategies.Tunnel);
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

        // A window that was never on screen has no real placement to remember.
        if (_hasBeenShown && WindowState == WindowState.Normal)
        {
            ui.Left = Position.X;
            ui.Top = Position.Y;
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
        var screens = Screens.All;
        if (screens.Count > 0)
        {
            var left = screens.Min(s => s.Bounds.X);
            var top = screens.Min(s => s.Bounds.Y);
            var right = screens.Max(s => s.Bounds.Right);
            var bottom = screens.Max(s => s.Bounds.Bottom);

            savedLeft = Math.Clamp(savedLeft, left, Math.Max(left, right - 100));
            savedTop = Math.Clamp(savedTop, top, Math.Max(top, bottom - 60));
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint((int)savedLeft, (int)savedTop);
    }

    private async void OpenSettings(Guid? focusResourceId)
    {
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }

        var app = (App)Application.Current!;
        var settings = new SettingsWindow(new SettingsViewModel(_viewModel.Config, app.AskPass), focusResourceId);
        _settings = settings;
        try
        {
            await settings.ShowModalAsync(this);
            if (settings.Result is { } result) _viewModel.ApplyConfig(result);
        }
        finally
        {
            _settings = null;
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.ClickCount == 2) return;
        BeginMoveDrag(e);
    }

    // BeginResizeDrag does nothing on macOS, so the grip resizes by hand on every platform. The
    // top-left corner stays put meanwhile, which makes window coordinates a stable reference.
    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _resizeStart = e.GetPosition(this);
        _resizeStartSize = ClientSize;
        e.Pointer.Capture(ResizeGrip);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_resizeStart is not { } start) return;

        var current = e.GetPosition(this);
        Width = Math.Max(MinWidth, _resizeStartSize.Width + current.X - start.X);
        Height = Math.Max(MinHeight, _resizeStartSize.Height + current.Y - start.Y);
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        _resizeStart = null;
        e.Pointer.Capture(null);
    }

    private void OnGripCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _resizeStart = null;

    private void OnWindowWheel(object? sender, PointerWheelEventArgs e)
    {
        // Ctrl + wheel on Windows; Cmd or Ctrl on macOS.
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;

        // Trackpads report fractions of a notch, so the step follows the delta.
        var ui = _viewModel.Config.Ui;
        ui.Opacity = Math.Clamp(ui.Opacity + e.Delta.Y * 0.05, 0.35, 1.0);
        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var app = (App)Application.Current!;

        // The close button hides to tray, only the tray menu really exits. A close coming from the
        // system (logout, Cmd+Q) must go through, or it would hold up the logout.
        if (!app.IsShuttingDown && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        SaveGeometry();
        base.OnClosing(e);
    }
}
