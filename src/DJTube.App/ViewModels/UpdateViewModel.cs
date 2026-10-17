using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Localization;
using DJTube.App.Services;
using DJTube.Core.Updates;

namespace DJTube.App.ViewModels;

/// <summary>
/// Franja de aviso de actualizaciones: comprobación al arrancar o a mano, descarga con progreso
/// e instalación. El texto se compone en el idioma actual.
/// </summary>
public partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _service = new();
    private readonly Func<bool> _isPlaying;
    private AvailableUpdate? _update;
    private (string Key, object?[] Args) _message = ("", []);

    /// <summary>La franja está visible.</summary>
    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall), nameof(CanDownloadPage))]
    private bool _isBusy;

    [ObservableProperty]
    private double? _progress;

    [ObservableProperty]
    private bool _isError;

    public UpdateViewModel(Func<bool> isPlaying)
    {
        _isPlaying = isPlaying;
        Loc.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Message));
    }

    public string Message => _message.Key.Length == 0 ? "" : Loc.F(_message.Key, _message.Args);

    public bool HasUpdate => _update is not null;

    /// <summary>Instalado y con instalador en la release: se puede actualizar desde aquí.</summary>
    public bool CanInstall => !IsBusy && _update?.Installer is not null && UpdateService.IsInstalled;

    /// <summary>Versión portable (o release sin instalador): se abre la página de descarga.</summary>
    public bool CanDownloadPage => !IsBusy && _update is not null && !CanInstallCore;

    private bool CanInstallCore => _update?.Installer is not null && UpdateService.IsInstalled;

    /// <param name="manual">A mano (menú): también informa si no hay nada o si falla.</param>
    public async Task CheckAsync(bool manual)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        IsError = false;
        if (manual)
            Show("Update_Checking");
        try
        {
            _update = await _service.CheckAsync();
            if (_update is not null)
                Show("Update_Available", _update.Version.ToString(), UpdateService.CurrentVersion.ToString());
            else if (manual)
                Show("Update_UpToDate", UpdateService.CurrentVersion.ToString());
            else
                IsVisible = false;
        }
        catch (Exception ex)
        {
            // Al arrancar, sin conexión no se molesta; a mano sí se informa.
            if (manual)
                ShowError("Update_Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
            NotifyButtons();
        }
    }

    [RelayCommand]
    private void OpenNotes()
    {
        if (_update is not null)
            UpdateService.OpenPage(_update.PageUrl);
    }

    [RelayCommand]
    private void Dismiss() => IsVisible = false;

    [RelayCommand]
    private async Task Install()
    {
        if (_update?.Installer is not { } installer || IsBusy)
            return;
        if (_isPlaying() && MessageBox.Show(Loc.T("Update_ConfirmPlaying"), "DJTube",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        IsBusy = true;
        IsError = false;
        Progress = 0;
        Show("Update_Downloading", 0.0);
        try
        {
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                Show("Update_Downloading", p * 100);
            });
            string path = await _service.DownloadAsync(installer, progress);
            Show("Update_Installing");
            UpdateService.LaunchInstaller(path);
            App.ExitingForUpdate = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Progress = null;
            ShowError("Update_DownloadError", ex.Message);
            IsBusy = false;
            NotifyButtons();
        }
    }

    private void Show(string key, params object?[] args)
    {
        _message = (key, args);
        IsVisible = true;
        OnPropertyChanged(nameof(Message));
    }

    private void ShowError(string key, params object?[] args)
    {
        IsError = true;
        Show(key, args);
    }

    private void NotifyButtons()
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanDownloadPage));
    }
}
