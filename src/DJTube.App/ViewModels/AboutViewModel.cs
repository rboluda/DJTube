using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Localization;
using DJTube.App.Services;

namespace DJTube.App.ViewModels;

/// <summary>Datos de la ventana «Acerca de».</summary>
public partial class AboutViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public AboutViewModel(MainViewModel main)
    {
        _main = main;
    }

    public string VersionText => Loc.F("About_Version", AppInfo.Version);

    public string BuildText => AppInfo.Commit is { } commit ? Loc.F("About_Build", commit) : "";

    public string RuntimeText => Loc.F("About_Runtime", AppInfo.Runtime);

    public string Libraries => _main.Libraries;

    public string YtDlpStatus => _main.YtDlpStatus;

    public string ProjectUrl => AppInfo.ProjectUrl;

    [RelayCommand]
    private void OpenProject() => Process.Start(new ProcessStartInfo(AppInfo.ProjectUrl) { UseShellExecute = true });
}
