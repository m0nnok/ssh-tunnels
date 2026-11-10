using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SshTunneler.Models;
using SshTunneler.ViewModels;

namespace SshTunneler.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _updatingSecretBox;
    private bool _closeConfirmed;
    private bool _confirming;

    public SettingsWindow(SettingsViewModel viewModel, Guid? focusResourceId = null)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncSecretBox();

        if (focusResourceId is { } id)
        {
            var resource = viewModel.Resources.FirstOrDefault(r => r.Id == id);
            if (resource is not null)
            {
                viewModel.SelectedResource = resource;
                Tabs.SelectedIndex = 1;
                Opened += (_, _) => ResourceList.ScrollIntoView(resource);
            }
        }
    }

    /// <summary>Configuration to apply, set only when the user saved.</summary>
    public AppConfig? Result { get; private set; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedServer)) SyncSecretBox();
    }

    // The secret box is kept out of bindings so an empty box maps to "no secret" (null), exactly
    // as the change detection and the stored JSON expect.
    private void SyncSecretBox()
    {
        _updatingSecretBox = true;
        SecretBox.Text = _viewModel.SelectedServer?.Secret ?? "";
        _updatingSecretBox = false;
    }

    private void OnSecretChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingSecretBox || _viewModel.SelectedServer is null) return;
        var text = SecretBox.Text ?? "";
        _viewModel.SelectedServer.Secret = text.Length == 0 ? null : text;
    }

    private async void OnBrowseKey(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedServer is not { } server) return;

        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите приватный ключ",
            AllowMultiple = false,
            SuggestedStartLocation = Directory.Exists(sshDir)
                ? await StorageProvider.TryGetFolderFromPathAsync(sshDir)
                : null
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            server.KeyPath = path;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var error = _viewModel.Validate();
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }

        Result = _viewModel.BuildResult();
        _closeConfirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => _ = TryDiscardAsync();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Escape used to close the dialog outright, which silently threw away a half filled form.
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        _ = TryDiscardAsync();
    }

    private async Task TryDiscardAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        _closeConfirmed = true;
        Close();
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_viewModel.HasChanges) return true;
        if (_confirming) return false;

        _confirming = true;
        try
        {
            return await MessageDialog.AskYesNoAsync(
                this, "Изменения не сохранены. Закрыть настройки и потерять их?");
        }
        finally
        {
            _confirming = false;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing cannot wait for an answer, so cancel it and close again once the user agrees.
        // Only a close the user started asks: the system shutting the app down must not stall.
        if (!_closeConfirmed && e.CloseReason == WindowCloseReason.WindowClosing && _viewModel.HasChanges)
        {
            e.Cancel = true;
            _ = TryDiscardAsync();
            return;
        }

        base.OnClosing(e);
    }
}
