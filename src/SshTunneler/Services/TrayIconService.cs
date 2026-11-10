using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SshTunneler.Models;
using SshTunneler.ViewModels;

namespace SshTunneler.Services;

/// <summary>
/// Tray presence (the menu bar on macOS): quick access to every tunnel without showing the window.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Action _toggleWindow;
    private readonly Action _openSettings;
    private readonly Action _exit;
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenu _menu = new();
    private bool _lastActive;

    public TrayIconService(MainViewModel viewModel, Action toggleWindow, Action openSettings, Action exit)
    {
        _viewModel = viewModel;
        _toggleWindow = toggleWindow;
        _openSettings = openSettings;
        _exit = exit;

        _trayIcon = new TrayIcon
        {
            Icon = TrayIconFactory.Create(false),
            ToolTipText = "SSH Tunneler",
            Menu = _menu,
            IsVisible = true
        };

        // The icon carries its status in colour, so macOS must not flatten it into a template.
        MacOSProperties.SetIsTemplateIcon(_trayIcon, false);

        // A click toggles the window on Windows; macOS never raises it and opens the menu instead.
        _trayIcon.Clicked += (_, _) => _toggleWindow();
        TrayIcon.SetIcons(Application.Current!, [_trayIcon]);

        // Windows shows the menu as it is, without an opening event to rebuild it on, so the menu
        // follows every change instead. Status changes arrive on background threads.
        viewModel.Items.CollectionChanged += (_, _) => BuildMenu();
        viewModel.Tunnels.StatusChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
        BuildMenu();
    }

    private void Refresh()
    {
        BuildMenu();

        var anyActive = _viewModel.Tunnels.ActiveResourceIds.Any();
        if (anyActive == _lastActive) return;

        _lastActive = anyActive;
        _trayIcon.Icon = TrayIconFactory.Create(anyActive);
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(Item("Показать / скрыть окно", _toggleWindow));
        _menu.Items.Add(new NativeMenuItemSeparator());

        foreach (var item in _viewModel.Items)
        {
            var captured = item;
            var entry = new NativeMenuItem($"{item.Name} — {item.StatusText}")
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = item.State == TunnelState.Connected
            };
            entry.Click += async (_, _) => await _viewModel.ToggleAsync(captured);
            _menu.Items.Add(entry);
        }

        if (_viewModel.Items.Count > 0) _menu.Items.Add(new NativeMenuItemSeparator());
        _menu.Items.Add(Item("Отключить все", () => _ = _viewModel.Tunnels.DisconnectAllAsync()));
        _menu.Items.Add(Item("Настройки...", _openSettings));
        _menu.Items.Add(new NativeMenuItemSeparator());
        _menu.Items.Add(Item("Выход", _exit));
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    public void Dispose()
    {
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }
}
