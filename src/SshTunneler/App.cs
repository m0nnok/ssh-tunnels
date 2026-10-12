using System.Windows;
using SshTunneler.Models;
using SshTunneler.Services;
using SshTunneler.ViewModels;
using SshTunneler.Views;

namespace SshTunneler;

public sealed class App : Application
{
    private TunnelManager? _tunnels;
    private TrayIconService? _tray;
    private MainWindow? _window;
    private MainViewModel? _viewModel;

    public AskPassServer AskPass { get; } = new();

    public bool IsShuttingDown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute)
        });

        var store = new DpapiJsonConfigStore();
        var config = LoadConfig(store);

        _tunnels = new TunnelManager(config, AskPass);
        _viewModel = new MainViewModel(config, store, _tunnels);
        _window = new MainWindow(_viewModel);

        _tray = new TrayIconService(
            _viewModel,
            ToggleWindow,
            () => { _window.ShowFromTray(); _viewModel.OpenSettingsCommand.Execute(null); },
            ShutdownApp);

        AutoStartService.Sync(config.Ui.AutoStartWithWindows);

        var startHidden = config.Ui.StartMinimized ||
                          Environment.GetCommandLineArgs().Contains("--minimized");
        if (startHidden)
            _tray.ShowBalloon("SSH Tunneler", "Приложение свёрнуто в трей");
        else
            _window.Show();

        _ = _viewModel.ConnectAutoStartAsync();
    }

    public void ShutdownApp()
    {
        if (IsShuttingDown) return;
        IsShuttingDown = true;

        _window?.SaveGeometry();
        _tunnels?.DisconnectAllAsync().GetAwaiter().GetResult();
        _tunnels?.Dispose();
        _tray?.Dispose();
        AskPass.Dispose();
        _window?.Close();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        ShutdownApp();
    }

    private void ToggleWindow()
    {
        if (_window is null) return;

        if (_window.IsVisible)
            _window.HideToTray();
        else
            _window.ShowFromTray();
    }

    private static AppConfig LoadConfig(DpapiJsonConfigStore store)
    {
        try
        {
            return store.Load();
        }
        catch (ConfigLoadException ex)
        {
            var details = ex.QuarantinedPath is null
                ? ex.Message
                : $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Старый файл сохранён как:{Environment.NewLine}{ex.QuarantinedPath}";

            MessageBox.Show(
                $"{details}{Environment.NewLine}{Environment.NewLine}Файл настроек:{Environment.NewLine}{store.Path}" +
                $"{Environment.NewLine}{Environment.NewLine}Приложение продолжит работу с пустой конфигурацией.",
                "SSH Tunneler",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return new AppConfig();
        }
    }
}
