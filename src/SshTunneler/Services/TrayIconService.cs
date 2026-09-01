using System.Drawing;
using System.Windows.Forms;
using SshTunneler.Models;
using SshTunneler.ViewModels;

namespace SshTunneler.Services;

/// <summary>Tray presence: quick access to every tunnel without showing the window.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Action _toggleWindow;
    private readonly Action _openSettings;
    private readonly Action _exit;
    private readonly NotifyIcon _notifyIcon;
    private Icon _icon;
    private bool _lastActive;

    public TrayIconService(MainViewModel viewModel, Action toggleWindow, Action openSettings, Action exit)
    {
        _viewModel = viewModel;
        _toggleWindow = toggleWindow;
        _openSettings = openSettings;
        _exit = exit;

        _icon = TrayIconFactory.Create(false);
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "SSH Tunneler",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };

        _notifyIcon.DoubleClick += (_, _) => _toggleWindow();
        _notifyIcon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        // Status changes arrive on background threads, the tray icon must be touched on the UI one.
        viewModel.Tunnels.StatusChanged += (_, _) =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshIcon);
    }

    public void ShowBalloon(string title, string text)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void BuildMenu()
    {
        var menu = _notifyIcon.ContextMenuStrip!;
        menu.Items.Clear();
        menu.Items.Add("Показать / скрыть окно", null, (_, _) => _toggleWindow());
        menu.Items.Add(new ToolStripSeparator());

        foreach (var item in _viewModel.Items)
        {
            var entry = new ToolStripMenuItem($"{item.Name} — {item.StatusText}")
            {
                Checked = item.State == TunnelState.Connected,
                CheckOnClick = false
            };

            var captured = item;
            entry.Click += async (_, _) => await _viewModel.ToggleAsync(captured);
            menu.Items.Add(entry);
        }

        if (_viewModel.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Отключить все", null, async (_, _) => await _viewModel.Tunnels.DisconnectAllAsync());
        menu.Items.Add("Настройки...", null, (_, _) => _openSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => _exit());
    }

    private void RefreshIcon()
    {
        var anyActive = _viewModel.Tunnels.ActiveResourceIds.Any();
        if (anyActive == _lastActive) return;

        _lastActive = anyActive;
        var previous = _icon;
        _icon = TrayIconFactory.Create(anyActive);
        _notifyIcon.Icon = _icon;
        previous.Dispose();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
