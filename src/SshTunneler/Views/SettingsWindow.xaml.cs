using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SshTunneler.Models;
using SshTunneler.ViewModels;

namespace SshTunneler.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _updatingSecretBox;
    private bool _discardConfirmed;

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
                Loaded += (_, _) => ResourceList.ScrollIntoView(resource);
            }
        }
    }

    /// <summary>Configuration to apply, set only when the user saved.</summary>
    public AppConfig? Result { get; private set; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedServer)) SyncSecretBox();
    }

    private void SyncSecretBox()
    {
        _updatingSecretBox = true;
        SecretBox.Password = _viewModel.SelectedServer?.Secret ?? "";
        _updatingSecretBox = false;
    }

    private void OnSecretChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingSecretBox || _viewModel.SelectedServer is null) return;
        _viewModel.SelectedServer.Secret = SecretBox.Password.Length == 0 ? null : SecretBox.Password;
    }

    private void OnBrowseKey(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedServer is null) return;

        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var dialog = new OpenFileDialog
        {
            Title = "Выберите приватный ключ",
            InitialDirectory = Directory.Exists(sshDir) ? sshDir : null,
            CheckFileExists = true,
            Filter = "Все файлы (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
            _viewModel.SelectedServer.KeyPath = dialog.FileName;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var error = _viewModel.Validate();
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }

        Result = _viewModel.BuildResult();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => TryDiscard();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Escape used to close the dialog outright, which silently threw away a half filled form.
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        TryDiscard();
    }

    private void TryDiscard()
    {
        if (!ConfirmDiscard()) return;
        _discardConfirmed = true;
        DialogResult = false;
    }

    private bool ConfirmDiscard()
    {
        if (!_viewModel.HasChanges) return true;

        return MessageBox.Show(
            this,
            "Изменения не сохранены. Закрыть настройки и потерять их?",
            "SSH Tunneler",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DialogResult != true && !_discardConfirmed && !ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
