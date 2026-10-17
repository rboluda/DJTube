using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Services;
using DJTube.Core.Abstractions;
using DJTube.Core.Logic;
using DJTube.Core.Models;
using DJTube.Sources.Local;
using DJTube.Sources.YouTube;

namespace DJTube.App.ViewModels;

public partial class DeckViewModel : ObservableObject
{
    private const string EmptyTitle = "Sin pista";

    private readonly IDeck _deck;
    private readonly ITrackSource _localSource;
    private readonly YouTubeTrackSource _youTube;
    private readonly IFileDialogService _dialogs;
    private readonly CueController _cue;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private string _title = EmptyTitle;

    [ObservableProperty]
    private string _elapsed = TimeFormat.Format(0);

    [ObservableProperty]
    private string _remaining = "-" + TimeFormat.Format(0);

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private double _cueFraction;

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
    [NotifyPropertyChangedFor(nameof(PitchText), nameof(KeyText))]
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

    public DeckViewModel(
        string name, IDeck deck, ITrackSource localSource, YouTubeTrackSource youTube, IFileDialogService dialogs)
    {
        Name = name;
        _deck = deck;
        _localSource = localSource;
        _youTube = youTube;
        _dialogs = dialogs;
        _cue = new CueController(deck);
    }

    public string Name { get; }

    public string LoadStatus => LoadProgress is { } p ? $"Descargando… {p * 100:0} %" : "Preparando…";

    public bool IsLoadProgressKnown => LoadProgress is not null;

    public string PitchText => PitchMath.Format(PitchPercent);

    public string RangeText => $"±{PitchRange:0}%";

    /// <summary>Cambio de tono que provoca el pitch (sin key lock, como en un vinilo).</summary>
    public string KeyText => KeyLock || Math.Abs(PitchPercent) < 0.005
        ? "tono original"
        : $"tono {PitchMath.Semitones(PitchPercent):+0.0;-0.0} st";

    /// <summary>Rango ±16 % (si no, ±8 %).</summary>
    public bool IsWideRange
    {
        get => PitchRange == PitchMath.WideRange;
        set => PitchRange = value ? PitchMath.WideRange : PitchMath.DefaultRange;
    }

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
        double position = loaded ? _deck.PositionSeconds : 0;
        bool playing = _deck.IsPlaying;

        IsLoaded = loaded;
        IsPlaying = playing;
        Elapsed = TimeFormat.Format(position);
        Remaining = "-" + TimeFormat.Format(duration - position);
        Progress = duration > 0 ? position / duration : 0;
        CueFraction = duration > 0 ? _cue.CuePoint / duration : 0;
        IsCueLit = loaded && (_cue.IsPreviewing
            || (!playing && Math.Abs(position - _cue.CuePoint) <= CueController.OnCueTolerance));
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

    [RelayCommand]
    private void Seek(double fraction)
    {
        if (_deck.IsLoaded)
            _deck.Seek(fraction * _deck.DurationSeconds);
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
            Error = $"Formato no admitido: {System.IO.Path.GetExtension(path)}";
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
            Error = "No es un enlace a un vídeo de YouTube.";
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
            Error = "No es un enlace de YouTube ni un archivo de audio.";
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
        var progress = new Progress<double>(fraction =>
        {
            if (_loadCts == cts)
                LoadProgress = fraction;
        });

        try
        {
            using TrackMedia media = await source.OpenAsync(track, progress, cts.Token);
            LoadProgress = null;
            await _deck.LoadAsync(media, cts.Token);
            _cue.Reset();
            Title = FormatTitle(media.Track);
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
            }
            cts.Dispose();
        }
        Refresh();
    }

    private static string FormatTitle(TrackInfo? track) => track switch
    {
        null => EmptyTitle,
        { Artist: { Length: > 0 } artist } => $"{artist} — {track.Title}",
        _ => track.Title,
    };
}
