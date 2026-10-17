using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Controls;
using DJTube.App.Localization;
using DJTube.App.Services;
using DJTube.Audio.Bass;
using DJTube.Core.Abstractions;
using DJTube.Core.Localization;
using DJTube.Core.Logic;
using DJTube.Core.Models;
using DJTube.Sources.YouTube;

namespace DJTube.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAudioEngine _engine;
    private readonly YouTubeTrackSource _youTube;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private bool _initializing;

    // Estado de la salida y de yt-dlp; el texto se compone en el idioma actual.
    private string? _outputError;
    private (string Key, object? Arg) _ytDlpState = ("YtDlpChecking", null);

    [ObservableProperty]
    private LocalizedOption<AudioDevice>? _selectedDevice;

    [ObservableProperty]
    private bool _hasOutputError;

    [ObservableProperty]
    private bool _hasYtDlpError;

    /// <summary>Segundos visibles en la vista ampliada (zoom común a los dos platos).</summary>
    [ObservableProperty]
    private double _zoomSeconds;

    public MainViewModel(
        IAudioEngine engine,
        ITrackSource localSource,
        YouTubeTrackSource youTube,
        ITrackAnalyzer analyzer,
        IFileDialogService dialogs,
        SettingsService settingsService,
        AppSettings settings)
    {
        if (engine.Decks.Count < 2)
            throw new ArgumentException("The engine needs two decks.", nameof(engine));

        _engine = engine;
        _youTube = youTube;
        _settingsService = settingsService;
        _settings = settings;
        _zoomSeconds = Math.Clamp(settings.ZoomSeconds, ZoomWaveform.MinSecondsVisible, ZoomWaveform.MaxSecondsVisible);

        Func<double> delay = () => engine.OutputDelaySeconds;
        DeckA = new DeckViewModel("A", engine.Decks[0], localSource, youTube, analyzer, delay, dialogs);
        DeckB = new DeckViewModel("B", engine.Decks[1], localSource, youTube, analyzer, delay, dialogs);
        DeckA.SyncTarget = DeckB;
        DeckB.SyncTarget = DeckA;

        var curve = Enum.TryParse(settings.CrossfaderCurve, out CrossfaderCurveType saved) ? saved : CrossfaderCurveType.Linear;
        Mixer = new MixerViewModel(engine.Decks[0], engine.Decks[1], curve);
        Mixer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MixerViewModel.SelectedCurve))
                SaveSettings();
        };

        Loc.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(OutputStatus));
            OnPropertyChanged(nameof(YtDlpStatus));
            OnPropertyChanged(nameof(Libraries));
        };
    }

    public DeckViewModel DeckA { get; }

    public DeckViewModel DeckB { get; }

    public MixerViewModel Mixer { get; }

    public ObservableCollection<LocalizedOption<AudioDevice>> Devices { get; } = [];

    public string WindowTitle => AppInfo.Title;

    public string CacheFolder => _youTube.CacheFolder;

    /// <summary>Versiones de las DLL de BASS (para diagnóstico).</summary>
    public string Libraries => string.Join(Environment.NewLine,
        BassLibraryInfo.GetVersions().Select(l => $"{l.Library} {l.Version}"));

    public string OutputStatus
    {
        get
        {
            if (_outputError is not null)
                return Loc.F("OutputError", _outputError);
            if (_engine.OutputStatus is not { } s)
                return Loc.T("OutputStopped");
            return Loc.F("OutputStatus", s.DeviceName, s.SampleRate, s.Channels, s.LatencyMilliseconds);
        }
    }

    public string YtDlpStatus => _ytDlpState.Arg is null ? Loc.T(_ytDlpState.Key) : Loc.F(_ytDlpState.Key, _ytDlpState.Arg);

    public string Language => Loc.Instance.Language;

    public bool IsSpanish => Language == Loc.Spanish;

    public bool IsEnglish => Language == Loc.English;

    /// <summary>Enumera los dispositivos, arranca la salida (la guardada o la predeterminada) y comprueba yt-dlp.</summary>
    public void Initialize()
    {
        _initializing = true;
        try
        {
            Devices.Clear();
            foreach (var device in _engine.GetOutputDevices())
                Devices.Add(new LocalizedOption<AudioDevice>(device, DeviceLabel));
            SelectedDevice = Devices.FirstOrDefault(d => d.Value.Name == _settings.OutputDeviceName)
                ?? Devices.FirstOrDefault(d => d.Value.IsDefault)
                ?? Devices.FirstOrDefault();
        }
        finally
        {
            _initializing = false;
        }
        StartOutput();
        _ = RefreshYtDlpStatusAsync();
    }

    /// <summary>Se llama en cada fotograma.</summary>
    public void Refresh()
    {
        DeckA.Refresh();
        DeckB.Refresh();
    }

    [RelayCommand]
    private void SetLanguage(string code)
    {
        Loc.Instance.SetLanguage(code);
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(IsSpanish));
        OnPropertyChanged(nameof(IsEnglish));
        SaveSettings();
    }

    [RelayCommand]
    private void ZoomIn() => ZoomSeconds = Math.Max(ZoomWaveform.MinSecondsVisible, ZoomSeconds / 1.25);

    [RelayCommand]
    private void ZoomOut() => ZoomSeconds = Math.Min(ZoomWaveform.MaxSecondsVisible, ZoomSeconds * 1.25);

    partial void OnZoomSecondsChanged(double value) => SaveSettings();

    partial void OnSelectedDeviceChanged(LocalizedOption<AudioDevice>? value)
    {
        if (_initializing)
            return;
        StartOutput();
        SaveSettings();
    }

    [RelayCommand]
    private async Task UpdateYtDlp()
    {
        string? exe = YtDlp.Find();
        if (exe is null)
        {
            await RefreshYtDlpStatusAsync();
            return;
        }

        SetYtDlpState("YtDlpUpdating", null, error: false);
        try
        {
            await YtDlp.UpdateAsync(exe);
            await RefreshYtDlpStatusAsync();
        }
        catch (Exception ex)
        {
            SetYtDlpState("YtDlpUpdateError", ex.Message, error: true);
        }
    }

    [RelayCommand]
    private void OpenCache()
    {
        System.IO.Directory.CreateDirectory(CacheFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{CacheFolder}\"") { UseShellExecute = true });
    }

    /// <summary>Guarda las preferencias (al cambiar algo y al cerrar).</summary>
    public void SaveSettings()
    {
        if (_initializing)
            return;
        _settings.Language = Loc.Instance.Language;
        _settings.OutputDeviceName = SelectedDevice?.Value.Name ?? _settings.OutputDeviceName;
        _settings.ZoomSeconds = ZoomSeconds;
        _settings.CrossfaderCurve = Mixer.SelectedCurve.Value.ToString();
        _settingsService.Save(_settings);
    }

    private static string DeviceLabel(AudioDevice device) =>
        device.IsDefault ? Messages.DefaultDevice(device.Name) : device.Name;

    private async Task RefreshYtDlpStatusAsync()
    {
        try
        {
            string? exe = await Task.Run(YtDlp.Find);
            string? version = exe is null ? null : await YtDlp.GetVersionAsync(exe);
            if (version is null)
                SetYtDlpState("YtDlpNotFound", null, error: true);
            else
                SetYtDlpState("YtDlpVersion", version, error: false);
        }
        catch (Exception ex)
        {
            SetYtDlpState("YtDlpError", ex.Message, error: true);
        }
    }

    private void SetYtDlpState(string key, object? arg, bool error)
    {
        _ytDlpState = (key, arg);
        HasYtDlpError = error;
        OnPropertyChanged(nameof(YtDlpStatus));
    }

    private void StartOutput()
    {
        try
        {
            _engine.StartOutput(new AudioOutputSettings(SelectedDevice?.Value.Id));
            _outputError = null;
        }
        catch (Exception ex)
        {
            _outputError = ex.Message;
        }
        HasOutputError = _outputError is not null;
        OnPropertyChanged(nameof(OutputStatus));
    }
}
