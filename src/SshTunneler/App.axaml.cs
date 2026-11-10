using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using SshTunneler.Models;
using SshTunneler.Services;
using SshTunneler.ViewModels;
using SshTunneler.Views;

namespace SshTunneler;

public sealed partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private TunnelManager? _tunnels;
    private TrayIconService? _tray;
    private MainWindow? _window;
    private MainViewModel? _viewModel;

    public AskPassServer AskPass { get; } = new();

    public bool IsShuttingDown { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        _desktop = desktop;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Logout, restart or Cmd+Q: the system is closing the app, the tunnels go with it.
        desktop.ShutdownRequested += (_, _) => ReleaseResources();

        // Starting the app again from Finder reaches the running instance as a reopen request;
        // without a Dock icon this is the only way to get the window back besides the menu bar.
        if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen) _window?.ShowFromTray();
            };

        var store = EncryptedJsonConfigStore.CreateDefault();
        var config = await LoadConfigAsync(store);

        _tunnels = new TunnelManager(config, AskPass);
        _viewModel = new MainViewModel(config, store, _tunnels, CopyToClipboardAsync);
        _window = new MainWindow(_viewModel);

        _tray = new TrayIconService(
            _viewModel,
            ToggleWindow,
            () => { _window.ShowFromTray(); _viewModel.OpenSettingsCommand.Execute(null); },
            ShutdownApp);

        AutoStartService.Sync(config.Ui.AutoStartWithWindows);

        var startHidden = config.Ui.StartMinimized || desktop.Args?.Contains("--minimized") == true;
        if (!startHidden) _window.ShowFromTray();

        _ = _viewModel.ConnectAutoStartAsync();
    }

    public void ShutdownApp()
    {
        ReleaseResources();
        _desktop?.Shutdown();
    }

    private void ReleaseResources()
    {
        if (IsShuttingDown) return;
        IsShuttingDown = true;

        _window?.SaveGeometry();

        // Disconnecting waits on per-tunnel gates, and a connect in flight holds its gate while it
        // resumes on this very thread. So wait off the UI thread, and only for a while: Dispose
        // kills whatever ssh is still running anyway.
        if (_tunnels is { } tunnels)
            Task.Run(tunnels.DisconnectAllAsync).Wait(TimeSpan.FromSeconds(5));

        _tunnels?.Dispose();
        _tray?.Dispose();
        AskPass.Dispose();
    }

    private void ToggleWindow()
    {
        if (_window is null) return;

        if (_window.IsVisible)
            _window.HideToTray();
        else
            _window.ShowFromTray();
    }

    private async Task CopyToClipboardAsync(string text)
    {
        var clipboard = _window?.Clipboard ?? throw new InvalidOperationException("окно закрыто");
        await clipboard.SetTextAsync(text);
    }

    private static async Task<AppConfig> LoadConfigAsync(EncryptedJsonConfigStore store)
    {
        var nl = Environment.NewLine;
        while (true)
        {
            try
            {
                // Reading may wait on the keychain, which can ask for the login password.
                return await Task.Run(store.Load);
            }
            catch (ConfigLoadException ex) when (ex.QuarantinedPath is null)
            {
                // The file is in place but could not be read just now: a locked keychain, denied
                // access, a file held by another program. Running on an empty configuration would
                // save over it, so either try again or move it out of the way first.
                var retry = await MessageDialog.AskYesNoAsync(
                    null,
                    $"{ex.Message}{nl}{nl}Файл настроек:{nl}{store.Path}{nl}{nl}" +
                    "Повторить попытку? «Нет» — начать с пустой конфигурацией, " +
                    "а нечитаемый файл сохранить под другим именем.");
                if (retry) continue;

                if (store.SetAside() is not { } setAside)
                {
                    await MessageDialog.ShowAsync(
                        null, $"Не удалось переименовать файл настроек:{nl}{store.Path}{nl}{nl}Попробуйте ещё раз.");
                    continue;
                }

                await MessageDialog.ShowAsync(
                    null,
                    $"Старый файл сохранён как:{nl}{setAside}{nl}{nl}Приложение продолжит работу с пустой конфигурацией.");
                return new AppConfig();
            }
            catch (ConfigLoadException ex)
            {
                await MessageDialog.ShowAsync(
                    null,
                    $"{ex.Message}{nl}{nl}Старый файл сохранён как:{nl}{ex.QuarantinedPath}{nl}{nl}" +
                    $"Файл настроек:{nl}{store.Path}{nl}{nl}Приложение продолжит работу с пустой конфигурацией.");
                return new AppConfig();
            }
        }
    }
}
