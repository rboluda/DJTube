using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Services;
using DJTube.Core.Abstractions;
using DJTube.Core.Logic;
using DJTube.Core.Models;
using DJTube.Sources.Local;

namespace DJTube.App.ViewModels;

public partial class DeckViewModel : ObservableObject
{
    private const string EmptyTitle = "Sin pista";

    private readonly IDeck _deck;
    private readonly ITrackSource _localSource;
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

    public DeckViewModel(string name, IDeck deck, ITrackSource localSource, IFileDialogService dialogs)
    {
        Name = name;
        _deck = deck;
        _localSource = localSource;
        _dialogs = dialogs;
        _cue = new CueController(deck);
    }

    public string Name { get; }

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

    public async Task LoadTrackAsync(ITrackSource source, TrackInfo track)
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        IsLoading = true;
        Error = null;
        Title = $"Cargando «{track.Title}»…";
        try
        {
            using TrackMedia media = await source.OpenAsync(track, cts.Token);
            await _deck.LoadAsync(media, cts.Token);
            _cue.Reset();
            Title = string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Artist} — {track.Title}";
        }
        catch (OperationCanceledException)
        {
            // La sustituyó otra carga.
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Title = _deck.Track?.Title ?? EmptyTitle;
        }
        finally
        {
            if (_loadCts == cts)
            {
                IsLoading = false;
                _loadCts = null;
            }
            cts.Dispose();
        }
        Refresh();
    }
}
