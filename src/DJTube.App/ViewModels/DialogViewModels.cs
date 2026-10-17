using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Localization;
using DJTube.App.Services;

namespace DJTube.App.ViewModels;

/// <summary>Ventana de preferencias: los cambios se aplican al pulsar Aceptar.</summary>
public partial class PreferencesViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private LocalizedOption<OutputMode> _selectedOutputMode;

    public PreferencesViewModel(MainViewModel main)
    {
        _main = main;
        _selectedLanguage = Languages.First(l => l.Code == Loc.Instance.Language);
        _selectedOutputMode = main.OutputModes.First(m => m.Value == main.OutputMode);
    }

    public IReadOnlyList<LanguageOption> Languages => Loc.Languages;

    public IReadOnlyList<LocalizedOption<OutputMode>> OutputModes => _main.OutputModes;

    public string CacheFolder => _main.CacheFolder;

    public IRelayCommand OpenCacheCommand => _main.OpenCacheCommand;

    public void Apply()
    {
        if (SelectedLanguage.Code != Loc.Instance.Language)
            _main.SetLanguageCommand.Execute(SelectedLanguage.Code);
        if (SelectedOutputMode.Value != _main.OutputMode)
            _main.OutputMode = SelectedOutputMode.Value;
    }
}

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
