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

    [ObservableProperty]
    private bool _checkForUpdates;

    public PreferencesViewModel(MainViewModel main)
    {
        _main = main;
        _selectedLanguage = Languages.First(l => l.Code == Loc.Instance.Language);
        _selectedOutputMode = main.OutputModes.First(m => m.Value == main.OutputMode);
        _checkForUpdates = main.CheckForUpdates;
        _recordingsFolder = main.Recording.Folder;
    }

    public IReadOnlyList<LanguageOption> Languages => Loc.Languages;

    public IReadOnlyList<LocalizedOption<OutputMode>> OutputModes => _main.OutputModes;

    public string CacheFolder => _main.CacheFolder;

    public IRelayCommand OpenCacheCommand => _main.OpenCacheCommand;

    [ObservableProperty]
    private string _recordingsFolder = "";

    [RelayCommand]
    private void BrowseRecordingsFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("Pref_RecordingsFolder"),
            InitialDirectory = System.IO.Directory.Exists(RecordingsFolder) ? RecordingsFolder : null,
        };
        if (dialog.ShowDialog() == true)
            RecordingsFolder = dialog.FolderName;
    }

    public void Apply()
    {
        if (!string.Equals(RecordingsFolder, _main.Recording.Folder, StringComparison.OrdinalIgnoreCase))
            _main.Recording.Folder = RecordingsFolder;
        if (SelectedLanguage.Code != Loc.Instance.Language)
            _main.SetLanguageCommand.Execute(SelectedLanguage.Code);
        if (SelectedOutputMode.Value != _main.OutputMode)
            _main.OutputMode = SelectedOutputMode.Value;
        _main.CheckForUpdates = CheckForUpdates;
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
