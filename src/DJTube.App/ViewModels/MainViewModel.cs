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

public enum OutputMode
{
    /// <summary>Master y auriculares en dispositivos WASAPI distintos.</summary>
    Wasapi,

    /// <summary>Una tarjeta ASIO multicanal: master y auriculares en pares de canales.</summary>
    Asio,
}

public partial class MainViewModel : ObservableObject
{
    /// <summary>Pares de canales ASIO que se ofrecen (primer canal, base 0).</summary>
    private static readonly int[] AsioPairs = [0, 2, 4, 6];

    private readonly IAudioEngine _engine;
    private readonly YouTubeTrackSource _youTube;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private bool _initializing;

    // Estado de la salida y de yt-dlp; el texto se compone en el idioma actual.
    private string? _outputError;
    private (string Key, object? Arg) _ytDlpState = ("YtDlpChecking", null);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWasapiMode), nameof(IsAsioMode))]
    private OutputMode _outputMode;

    [ObservableProperty]
    private LocalizedOption<AudioDevice>? _selectedDevice;

    [ObservableProperty]
    private LocalizedOption<AudioDevice?>? _selectedCueDevice;

    [ObservableProperty]
    private LocalizedOption<AudioDevice>? _selectedAsioDevice;

    [ObservableProperty]
    private LocalizedOption<int>? _selectedMasterPair;

    [ObservableProperty]
    private LocalizedOption<int>? _selectedCuePair;

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
        _outputMode = Enum.TryParse(settings.OutputMode, out OutputMode mode) ? mode : OutputMode.Wasapi;

        Func<double> delay = () => engine.OutputDelaySeconds;
        DeckA = new DeckViewModel("A", engine.Decks[0], localSource, youTube, analyzer, delay, dialogs);
        DeckB = new DeckViewModel("B", engine.Decks[1], localSource, youTube, analyzer, delay, dialogs);
        DeckA.SyncTarget = DeckB;
        DeckB.SyncTarget = DeckA;

        var curve = Enum.TryParse(settings.CrossfaderCurve, out CrossfaderCurveType saved) ? saved : CrossfaderCurveType.Linear;
        Mixer = new MixerViewModel(engine, curve, settings.CueMix, settings.HeadphoneVolume);
        Mixer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MixerViewModel.SelectedCurve) or nameof(MixerViewModel.CueMix)
                or nameof(MixerViewModel.HeadphoneVolume))
                SaveSettings();
        };

        Update = new UpdateViewModel(() => DeckA.IsPlaying || DeckB.IsPlaying);
        _checkForUpdates = settings.CheckForUpdates;

        Library = new LibraryViewModel(youTube, analyzer, DeckA, DeckB);
        _showLibrary = settings.ShowLibrary;
        _libraryHeight = Math.Clamp(settings.LibraryHeight, MinLibraryHeight, 2000);

        MasterPairs = AsioPairs.Select(PairOption).ToList();
        CuePairs = [new LocalizedOption<int>(-1, _ => Loc.T("NoHeadphones")), .. AsioPairs.Select(PairOption)];
        OutputModes =
        [
            new(OutputMode.Wasapi, _ => Loc.T("Mode_Wasapi")),
            new(OutputMode.Asio, _ => Loc.T("Mode_Asio")),
        ];

        Loc.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(OutputStatus));
            OnPropertyChanged(nameof(CueStatus));
            OnPropertyChanged(nameof(YtDlpStatus));
            OnPropertyChanged(nameof(Libraries));
        };
    }

    public DeckViewModel DeckA { get; }

    public DeckViewModel DeckB { get; }

    public MixerViewModel Mixer { get; }

    /// <summary>Aviso de actualizaciones.</summary>
    public UpdateViewModel Update { get; }

    /// <summary>Canciones ya descargadas.</summary>
    public LibraryViewModel Library { get; }

    public const double MinLibraryHeight = 120;

    [ObservableProperty]
    private bool _showLibrary;

    /// <summary>Alto de la biblioteca en píxeles (se ajusta con el divisor).</summary>
    [ObservableProperty]
    private double _libraryHeight;

    partial void OnShowLibraryChanged(bool value) => SaveSettings();

    [RelayCommand]
    private void ToggleLibrary() => ShowLibrary = !ShowLibrary;

    /// <summary>Buscar actualizaciones al arrancar (Preferencias).</summary>
    [ObservableProperty]
    private bool _checkForUpdates;

    partial void OnCheckForUpdatesChanged(bool value) => SaveSettings();

    [RelayCommand]
    private Task CheckUpdatesNow() => Update.CheckAsync(manual: true);

    /// <summary>Dispositivos WASAPI para el master.</summary>
    public ObservableCollection<LocalizedOption<AudioDevice>> Devices { get; } = [];

    /// <summary>Dispositivos WASAPI para los auriculares (el primero es «ninguno»).</summary>
    public ObservableCollection<LocalizedOption<AudioDevice?>> CueDevices { get; } = [];

    public ObservableCollection<LocalizedOption<AudioDevice>> AsioDevices { get; } = [];

    public IReadOnlyList<LocalizedOption<int>> MasterPairs { get; }

    public IReadOnlyList<LocalizedOption<int>> CuePairs { get; }

    public IReadOnlyList<LocalizedOption<OutputMode>> OutputModes { get; }

    public bool IsWasapiMode => OutputMode == OutputMode.Wasapi;

    public bool IsAsioMode => OutputMode == OutputMode.Asio;

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
            return Loc.F("OutputStatus", s.Driver, s.DeviceName, s.SampleRate, s.Channels, s.LatencyMilliseconds);
        }
    }

    /// <summary>Estado de la salida de auriculares ("" si no hay).</summary>
    public string CueStatus
    {
        get
        {
            if (_engine.CueOutputError is { } error)
                return Loc.F("CueError", error);
            if (_engine.CueOutputStatus is not { } s)
                return "";
            return Loc.F("CueStatus", s.DeviceName, s.SampleRate, s.LatencyMilliseconds);
        }
    }

    public bool HasCueError => _engine.CueOutputError is not null;

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
            CueDevices.Clear();
            CueDevices.Add(new LocalizedOption<AudioDevice?>(null, _ => Loc.T("NoHeadphones")));
            foreach (var device in _engine.GetOutputDevices())
            {
                Devices.Add(new LocalizedOption<AudioDevice>(device, DeviceLabel));
                CueDevices.Add(new LocalizedOption<AudioDevice?>(device, d => DeviceLabel(d!)));
            }
            SelectedDevice = Devices.FirstOrDefault(d => d.Value.Name == _settings.OutputDeviceName)
                ?? Devices.FirstOrDefault(d => d.Value.IsDefault)
                ?? Devices.FirstOrDefault();
            SelectedCueDevice = CueDevices.FirstOrDefault(d => d.Value?.Name == _settings.CueDeviceName) ?? CueDevices[0];

            AsioDevices.Clear();
            foreach (var device in _engine.GetAsioDevices())
                AsioDevices.Add(new LocalizedOption<AudioDevice>(device, d => d.Name));
            SelectedAsioDevice = AsioDevices.FirstOrDefault(d => d.Value.Name == _settings.AsioDeviceName)
                ?? AsioDevices.FirstOrDefault();
            SelectedMasterPair = MasterPairs.FirstOrDefault(p => p.Value == _settings.AsioMasterChannel) ?? MasterPairs[0];
            SelectedCuePair = CuePairs.FirstOrDefault(p => p.Value == _settings.AsioCueChannel) ?? CuePairs[2];
        }
        finally
        {
            _initializing = false;
        }
        StartOutput();
        _ = Library.LoadAsync();
        _ = RefreshYtDlpStatusAsync();
        if (CheckForUpdates)
            _ = CheckUpdatesOnStartupAsync();
    }

    private async Task CheckUpdatesOnStartupAsync()
    {
        // Un poco después de arrancar, para no competir con la apertura de la salida de audio.
        await Task.Delay(TimeSpan.FromSeconds(3));
        await Update.CheckAsync(manual: false);
    }

    /// <summary>Se llama en cada fotograma.</summary>
    public void Refresh()
    {
        _engine.Maintain();
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

    partial void OnOutputModeChanged(OutputMode value) => OutputSelectionChanged();

    partial void OnSelectedDeviceChanged(LocalizedOption<AudioDevice>? value) => OutputSelectionChanged();

    partial void OnSelectedCueDeviceChanged(LocalizedOption<AudioDevice?>? value) => OutputSelectionChanged();

    partial void OnSelectedAsioDeviceChanged(LocalizedOption<AudioDevice>? value) => OutputSelectionChanged();

    partial void OnSelectedMasterPairChanged(LocalizedOption<int>? value) => OutputSelectionChanged();

    partial void OnSelectedCuePairChanged(LocalizedOption<int>? value) => OutputSelectionChanged();

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
        _settings.OutputMode = OutputMode.ToString();
        _settings.OutputDeviceName = SelectedDevice?.Value.Name ?? _settings.OutputDeviceName;
        _settings.CueDeviceName = SelectedCueDevice?.Value?.Name;
        _settings.AsioDeviceName = SelectedAsioDevice?.Value.Name ?? _settings.AsioDeviceName;
        _settings.AsioMasterChannel = SelectedMasterPair?.Value ?? 0;
        _settings.AsioCueChannel = SelectedCuePair?.Value ?? 2;
        _settings.ZoomSeconds = ZoomSeconds;
        _settings.CrossfaderCurve = Mixer.SelectedCurve.Value.ToString();
        _settings.CueMix = Mixer.CueMix;
        _settings.HeadphoneVolume = Mixer.HeadphoneVolume;
        _settings.CheckForUpdates = CheckForUpdates;
        _settings.ShowLibrary = ShowLibrary;
        _settings.LibraryHeight = LibraryHeight;
        _settingsService.Save(_settings);
    }

    private static string DeviceLabel(AudioDevice device) =>
        device.IsDefault ? Messages.DefaultDevice(device.Name) : device.Name;

    private static LocalizedOption<int> PairOption(int first) =>
        new(first, f => Loc.F("ChannelPair", f + 1, f + 2));

    private void OutputSelectionChanged()
    {
        if (_initializing)
            return;
        StartOutput();
        SaveSettings();
    }

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
            if (OutputMode == OutputMode.Asio)
            {
                if (SelectedAsioDevice is not { } asio)
                {
                    _engine.StopOutput();
                    _outputError = Loc.T("NoAsioDrivers");
                }
                else
                {
                    _engine.StartOutput(new AsioOutputConfig(
                        asio.Value.Id, SelectedMasterPair?.Value ?? 0, SelectedCuePair?.Value ?? -1));
                    _outputError = null;
                }
            }
            else
            {
                _engine.StartOutput(new WasapiOutputConfig(SelectedDevice?.Value.Id, SelectedCueDevice?.Value?.Id));
                _outputError = null;
            }
        }
        catch (Exception ex)
        {
            _outputError = ex.Message;
        }
        HasOutputError = _outputError is not null;
        OnPropertyChanged(nameof(OutputStatus));
        OnPropertyChanged(nameof(CueStatus));
        OnPropertyChanged(nameof(HasCueError));
    }
}
