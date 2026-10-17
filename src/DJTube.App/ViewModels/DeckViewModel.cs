using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Localization;
using DJTube.App.Services;
using DJTube.Core.Abstractions;
using DJTube.Core.Logic;
using DJTube.Core.Models;
using DJTube.Sources.Local;
using DJTube.Sources.YouTube;

namespace DJTube.App.ViewModels;

public partial class DeckViewModel : ObservableObject
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private readonly IDeck _deck;
    private readonly ITrackSource _localSource;
    private readonly YouTubeTrackSource _youTube;
    private readonly ITrackAnalyzer _analyzer;
    private readonly Func<double> _outputDelaySeconds;
    private readonly IFileDialogService _dialogs;
    private readonly CueController _cue;
    private readonly PlayheadSmoother _smoother = new();
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _analysisCts;
    private int _artworkGeneration;

    [ObservableProperty]
    private string _title = Loc.T("NoTrack");

    [ObservableProperty]
    private string _elapsed = TimeFormat.Format(0);

    [ObservableProperty]
    private string _remaining = "-" + TimeFormat.Format(0);

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private double _cueFraction;

    /// <summary>Posición suavizada para dibujar (segundos de pista).</summary>
    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _cuePoint;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>Piloto del botón CUE: en pausa sobre el punto de cue, o reproduciendo con CUE pulsado.</summary>
    [ObservableProperty]
    private bool _isCueLit;

    [ObservableProperty]
    private string? _error;

    /// <summary>Pitch en %; positivo = más rápido.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PitchText), nameof(KeyText), nameof(PlaybackRate), nameof(BpmText))]
    private double _pitchPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWideRange), nameof(RangeText))]
    private double _pitchRange = PitchMath.DefaultRange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyText))]
    private bool _keyLock;

    /// <summary>Texto del campo de enlace de YouTube.</summary>
    [ObservableProperty]
    private string _linkText = "";

    /// <summary>Progreso de la descarga (0..1), o null si no se conoce.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadStatus), nameof(IsLoadProgressKnown))]
    private double? _loadProgress;

    [ObservableProperty]
    private WaveformData? _waveform;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BpmText), nameof(OriginalBpmText), nameof(HasGrid))]
    [NotifyCanExecuteChangedFor(nameof(HalveBpmCommand), nameof(DoubleBpmCommand))]
    private BeatGrid? _grid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BpmText), nameof(AnalysisStatus))]
    private bool _isAnalyzing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnalysisStatus))]
    private double _analysisProgress;

    /// <summary>Miniatura del vídeo (o carátula) para reconocer la pista.</summary>
    [ObservableProperty]
    private ImageSource? _artwork;

    public DeckViewModel(
        string name,
        IDeck deck,
        ITrackSource localSource,
        YouTubeTrackSource youTube,
        ITrackAnalyzer analyzer,
        Func<double> outputDelaySeconds,
        IFileDialogService dialogs)
    {
        Name = name;
        _deck = deck;
        _localSource = localSource;
        _youTube = youTube;
        _analyzer = analyzer;
        _outputDelaySeconds = outputDelaySeconds;
        _dialogs = dialogs;
        _cue = new CueController(deck);
        Loc.Instance.LanguageChanged += (_, _) => OnLanguageChanged();
    }

    /// <summary>Recalcula los textos que dependen del idioma.</summary>
    private void OnLanguageChanged()
    {
        if (!IsLoading)
            Title = FormatTitle(_deck.Track);
        OnPropertyChanged(nameof(LoadStatus));
        OnPropertyChanged(nameof(OriginalBpmText));
        OnPropertyChanged(nameof(AnalysisStatus));
        OnPropertyChanged(nameof(KeyText));
    }

    public string Name { get; }

    /// <summary>Plato con el que se sincroniza este al pulsar SYNC.</summary>
    public DeckViewModel? SyncTarget { get; set; }

    public string LoadStatus => LoadProgress is { } p ? Loc.F("Downloading", p * 100) : Loc.T("Preparing");

    public bool IsLoadProgressKnown => LoadProgress is not null;

    public string PitchText => PitchMath.Format(PitchPercent);

    public string RangeText => $"±{PitchRange:0}%";

    public double PlaybackRate => PitchMath.ToRate(PitchPercent);

    public bool HasGrid => Grid is not null;

    /// <summary>BPM que suena (con el pitch aplicado).</summary>
    public string BpmText => Grid is { } g
        ? PitchMath.EffectiveBpm(g.Bpm, PitchPercent).ToString("0.00", CultureInfo.InvariantCulture)
        : IsAnalyzing ? "···" : "—";

    public string OriginalBpmText => Grid is { } g
        ? Loc.F("OriginalBpm", g.Bpm.ToString("0.00", CultureInfo.InvariantCulture))
        : "";

    public string AnalysisStatus => IsAnalyzing ? Loc.F("Analyzing", AnalysisProgress * 100) : "";

    /// <summary>Cambio de tono que provoca el pitch (sin key lock, como en un vinilo).</summary>
    public string KeyText => KeyLock || Math.Abs(PitchPercent) < 0.005
        ? Loc.T("OriginalKey")
        : Loc.F("KeyShift", PitchMath.Semitones(PitchPercent));

    /// <summary>Rango ±16 % (si no, ±8 %).</summary>
    public bool IsWideRange
    {
        get => PitchRange == PitchMath.WideRange;
        set => PitchRange = value ? PitchMath.WideRange : PitchMath.DefaultRange;
    }

    /// <summary>Posición real (sin suavizar) que se oye; la usa SYNC.</summary>
    internal double HeardPosition => _deck.PositionSeconds;

    internal bool IsDeckPlaying => _deck.IsPlaying;

    partial void OnPitchPercentChanged(double value)
    {
        double clamped = PitchMath.Clamp(value, PitchRange);
        if (clamped != value)
        {
            PitchPercent = clamped;
            return;
        }
        _deck.PitchPercent = value;
    }

    partial void OnPitchRangeChanged(double value) => PitchPercent = PitchMath.Clamp(PitchPercent, value);

    partial void OnKeyLockChanged(bool value) => _deck.KeyLock = value;

    [RelayCommand]
    private void ResetPitch() => PitchPercent = 0;

    /// <summary>Ajuste fino del pitch (rueda del ratón).</summary>
    public void NudgePitch(double delta) => PitchPercent = PitchMath.Clamp(PitchPercent + delta, PitchRange);

    /// <summary>Lee el estado del plato. Se llama en cada fotograma.</summary>
    public void Refresh()
    {
        bool loaded = _deck.IsLoaded;
        double duration = _deck.DurationSeconds;
        bool playing = _deck.IsPlaying;
        double reported = loaded ? _deck.PositionSeconds : 0;
        double position = _smoother.Update(reported, Clock.Elapsed.TotalSeconds, PlaybackRate, playing);

        IsLoaded = loaded;
        IsPlaying = playing;
        PositionSeconds = position;
        CuePoint = _cue.CuePoint;
        Elapsed = TimeFormat.Format(position);
        Remaining = "-" + TimeFormat.Format(duration - position);
        Progress = duration > 0 ? position / duration : 0;
        CueFraction = duration > 0 ? _cue.CuePoint / duration : 0;
        IsCueLit = loaded && (_cue.IsPreviewing
            || (!playing && Math.Abs(reported - _cue.CuePoint) <= CueController.OnCueTolerance));
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (!_deck.IsLoaded)
            return;
        _cue.PlayPause();
    }

    [RelayCommand]
    private void CueDown()
    {
        if (_deck.IsLoaded)
            _cue.CueDown();
    }

    [RelayCommand]
    private void CueUp() => _cue.CueUp();

    /// <summary>Salto a una fracción de la pista (onda completa).</summary>
    [RelayCommand]
    private void Seek(double fraction)
    {
        if (_deck.IsLoaded)
            _deck.Seek(fraction * _deck.DurationSeconds);
    }

    /// <summary>Salto a una posición en segundos (arrastrar la vista ampliada).</summary>
    [RelayCommand]
    private void SeekSeconds(double seconds)
    {
        if (_deck.IsLoaded)
            _deck.Seek(seconds);
    }

    /// <summary>
    /// Iguala el tempo con el otro plato y, si los dos suenan, alinea los golpes.
    /// Si el pitch necesario no cabe en ±8 %, pasa a ±16 %.
    /// </summary>
    [RelayCommand]
    private void Sync()
    {
        if (SyncTarget is not { } master || !_deck.IsLoaded || !master.IsLoaded)
        {
            Error = Loc.T("Err_SyncNeedsTracks");
            return;
        }
        if (Grid is not { } grid || master.Grid is not { } masterGrid)
        {
            Error = Loc.T("Err_SyncNeedsBpm");
            return;
        }

        double latency = _outputDelaySeconds();
        SyncPlan? Plan(double range) => SyncCalculator.Plan(
            masterGrid, master.HeardPosition, master.PitchPercent, master.IsDeckPlaying,
            grid, _deck.PositionSeconds, PitchPercent, _deck.IsPlaying, range, latency);

        var plan = Plan(PitchRange);
        if (plan is null && PitchRange < PitchMath.WideRange && Plan(PitchMath.WideRange) is { } wide)
        {
            PitchRange = PitchMath.WideRange;
            plan = wide;
        }
        if (plan is null)
        {
            Error = Loc.F("Err_SyncOutOfRange", master.Name);
            return;
        }

        Error = null;
        PitchPercent = plan.PitchPercent;
        if (plan.SeekSeconds is { } seek)
            _deck.Seek(seek);
    }

    /// <summary>Corrige un BPM detectado al doble (p. ej. 174 → 87).</summary>
    [RelayCommand(CanExecute = nameof(HasGrid))]
    private void HalveBpm()
    {
        if (Grid is { } g && g.Bpm / 2 >= 40)
            Grid = new BeatGrid(g.Bpm / 2, g.FirstBeatSeconds);
    }

    /// <summary>Corrige un BPM detectado a la mitad (p. ej. 70 → 140).</summary>
    [RelayCommand(CanExecute = nameof(HasGrid))]
    private void DoubleBpm()
    {
        if (Grid is { } g && g.Bpm * 2 <= 300)
            Grid = new BeatGrid(g.Bpm * 2, g.FirstBeatSeconds);
    }

    [RelayCommand]
    private async Task LoadFile()
    {
        string? path = _dialogs.OpenAudioFile();
        if (path is not null)
            await LoadFileAsync(path);
    }

    public async Task LoadFileAsync(string path)
    {
        if (!LocalFileTrackSource.IsSupported(path))
        {
            Error = Loc.F("Err_UnsupportedFormat", System.IO.Path.GetExtension(path));
            return;
        }
        await LoadTrackAsync(_localSource, LocalFileTrackSource.CreateTrackInfo(path));
    }

    /// <summary>Carga el enlace de YouTube escrito en el campo de la pletina.</summary>
    [RelayCommand]
    private async Task LoadLink()
    {
        string text = LinkText.Trim();
        if (text.Length == 0)
            return;
        if (!YouTubeUrl.TryGetVideoId(text, out _))
        {
            Error = Loc.T("Err_NotYouTubeLink");
            return;
        }
        LinkText = "";
        await LoadTextAsync(text);
    }

    /// <summary>Carga un enlace de YouTube o la ruta de un archivo (al soltar texto o archivos en la pletina).</summary>
    public async Task LoadTextAsync(string text)
    {
        text = text.Trim();
        if (YouTubeUrl.TryGetVideoId(text, out string videoId))
            await LoadTrackAsync(_youTube, _youTube.CreateTrackInfo(videoId));
        else if (System.IO.File.Exists(text))
            await LoadFileAsync(text);
        else
            Error = Loc.T("Err_NotLinkOrFile");
    }

    [RelayCommand]
    private void CancelLoad() => _loadCts?.Cancel();

    public async Task LoadTrackAsync(ITrackSource source, TrackInfo track)
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        IsLoading = true;
        LoadProgress = null;
        Error = null;
        Title = FormatTitle(track);
        var previousArtwork = Artwork;
        int artworkGeneration = ++_artworkGeneration;
        Artwork = null;
        _ = ShowArtworkAsync(source, track, artworkGeneration);

        var progress = new Progress<double>(fraction =>
        {
            if (_loadCts == cts)
                LoadProgress = fraction;
        });

        bool loaded = false;
        try
        {
            using TrackMedia media = await source.OpenAsync(track, progress, cts.Token);
            LoadProgress = null;
            await _deck.LoadAsync(media, cts.Token);
            _cue.Reset();
            Title = FormatTitle(media.Track);
            loaded = true;
            if (media.FilePath is { } path)
                StartAnalysis(path);
        }
        catch (OperationCanceledException)
        {
            // Si la canceló el usuario (no otra carga), se vuelve a mostrar la pista que hay en el plato.
            if (_loadCts == cts)
                Title = FormatTitle(_deck.Track);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Title = FormatTitle(_deck.Track);
        }
        finally
        {
            if (_loadCts == cts)
            {
                IsLoading = false;
                LoadProgress = null;
                _loadCts = null;
                if (!loaded)
                {
                    // La pista del plato no ha cambiado: se recupera su imagen.
                    _artworkGeneration++;
                    Artwork = previousArtwork;
                }
            }
            cts.Dispose();
        }
        Refresh();
    }

    /// <summary>Calcula la forma de onda y el BPM en segundo plano; el audio ya suena mientras tanto.</summary>
    private async void StartAnalysis(string path)
    {
        _analysisCts?.Cancel();
        var cts = new CancellationTokenSource();
        _analysisCts = cts;

        Waveform = null;
        Grid = null;
        AnalysisProgress = 0;
        IsAnalyzing = true;
        var progress = new Progress<double>(p =>
        {
            if (_analysisCts == cts)
                AnalysisProgress = p;
        });

        try
        {
            var analysis = await _analyzer.AnalyzeAsync(path, progress, cts.Token);
            if (_analysisCts != cts)
                return;
            Waveform = analysis.Waveform;
            Grid = analysis.Grid;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_analysisCts == cts)
                Error = Loc.F("Err_Analysis", ex.Message);
        }
        finally
        {
            if (_analysisCts == cts)
            {
                IsAnalyzing = false;
                _analysisCts = null;
            }
            cts.Dispose();
        }
    }

    private async Task ShowArtworkAsync(ITrackSource source, TrackInfo track, int generation)
    {
        try
        {
            string? path = await source.GetArtworkAsync(track);
            if (path is null)
                return;
            var image = await Task.Run(() => LoadImage(path));
            if (generation == _artworkGeneration && image is not null)
                Artwork = image;
        }
        catch (Exception)
        {
            // La imagen es opcional.
        }
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 320;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string FormatTitle(TrackInfo? track) => track switch
    {
        null => Loc.T("NoTrack"),
        { Artist: { Length: > 0 } artist } => $"{artist} — {track.Title}",
        _ => track.Title,
    };
}
