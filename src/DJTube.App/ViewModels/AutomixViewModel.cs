using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Localization;
using DJTube.App.Services;
using DJTube.Core.Logic;
using DJTube.Sources.YouTube;

namespace DJTube.App.ViewModels;

/// <summary>Una canción en la cola del automix.</summary>
public sealed record AutomixItem(string Id, string Display);

/// <summary>
/// Automix: pincha solo la cola. Cuando a la canción que suena le queda poco, carga la siguiente en el otro
/// plato, la arranca en su primer golpe (al mismo tempo si cabe en el rango de pitch, y en fase) y hace el
/// fundido con el crossfader. Pensado para los descansos.
/// </summary>
public partial class AutomixViewModel : ObservableObject
{
    private enum Phase
    {
        Playing,
        Fading,
    }

    private readonly DeckViewModel _deckA;
    private readonly DeckViewModel _deckB;
    private readonly MixerViewModel _mixer;
    private readonly YouTubeTrackSource _source;
    private readonly AppSettings _settings;

    private Phase _phase;
    private DeckViewModel? _current;
    private DeckViewModel? _next;
    private bool _preparing;
    private DateTime _fadeStart;
    private bool _towardsB;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsVisible))]
    private bool _isOn;

    public AutomixViewModel(DeckViewModel deckA, DeckViewModel deckB, MixerViewModel mixer, YouTubeTrackSource source, AppSettings settings)
    {
        _deckA = deckA;
        _deckB = deckB;
        _mixer = mixer;
        _source = source;
        _settings = settings;
        Queue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsVisible));
        };
        Loc.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(StatusText));
    }

    public ObservableCollection<AutomixItem> Queue { get; } = [];

    /// <summary>La franja del automix se ve si está encendido o hay algo en cola.</summary>
    public bool IsVisible => IsOn || Queue.Count > 0;

    private double FadeSeconds => Math.Clamp(_settings.AutomixFadeSeconds, AutomixMath.MinFadeSeconds, AutomixMath.MaxFadeSeconds);

    public string StatusText => (IsOn, Queue.Count) switch
    {
        (true, 0) => Loc.T("Automix_LastSong"),
        (true, _) => Loc.F("Automix_Next", Queue[0].Display, Queue.Count),
        (false, _) => Queue.Count == 1 ? Loc.T("Automix_QueuedOne") : Loc.F("Automix_Queued", Queue.Count),
    };

    public void Enqueue(IEnumerable<AutomixItem> items)
    {
        foreach (var item in items)
            Queue.Add(item);
    }

    [RelayCommand]
    private void Clear()
    {
        Queue.Clear();
        if (IsOn && _next is null && !_preparing && _current is { IsPlaying: false })
            IsOn = false;
    }

    [RelayCommand]
    private async Task Toggle()
    {
        if (IsOn)
        {
            Stop();
            return;
        }
        await StartAsync();
    }

    /// <summary>Pasa ya a la siguiente (empieza el fundido sin esperar al final).</summary>
    [RelayCommand]
    private async Task Skip()
    {
        if (!IsOn || _phase == Phase.Fading)
            return;
        if (_next is null && Queue.Count > 0)
            await PrepareNextAsync();
        if (_next is { IsLoaded: true })
            StartFade();
    }

    public void Stop()
    {
        IsOn = false;
        _phase = Phase.Playing;
        _next = null;
    }

    private async Task StartAsync()
    {
        IsOn = true;
        _phase = Phase.Playing;
        _next = null;

        // Si ya suena algo, el automix sigue desde ahí; si no, empieza con la primera de la cola en A.
        _current = (_deckA.IsPlaying, _deckB.IsPlaying) switch
        {
            (true, true) => _mixer.Crossfader <= 0 ? _deckA : _deckB,
            (true, false) => _deckA,
            (false, true) => _deckB,
            _ => null,
        };
        if (_current is not null)
            return;
        if (Queue.Count == 0)
        {
            IsOn = false;
            return;
        }
        var first = Queue[0];
        Queue.RemoveAt(0);
        await _deckA.LoadTrackAsync(_source, _source.CreateTrackInfo(first.Id));
        _mixer.Crossfader = -1;
        _deckA.SeekTo(AutomixMath.StartPosition(_deckA.Grid));
        _deckA.PlayNow();
        _current = _deckA;
    }

    /// <summary>Se llama en cada fotograma.</summary>
    public void Refresh()
    {
        if (!IsOn || _current is not { } current)
            return;

        if (_phase == Phase.Fading)
        {
            double progress = (DateTime.Now - _fadeStart).TotalSeconds / FadeSeconds;
            _mixer.Crossfader = AutomixMath.Crossfader(progress, _towardsB);
            if (progress >= 1)
            {
                current.PauseNow();
                _current = _next;
                _next = null;
                _phase = Phase.Playing;
            }
            return;
        }

        double remaining = AutomixMath.RemainingSeconds(current.DurationSeconds, current.HeardPosition, current.PlaybackRate);
        if (_next is null && !_preparing && Queue.Count > 0 && AutomixMath.ShouldPrepare(remaining, FadeSeconds))
            _ = PrepareNextAsync();

        bool ended = !current.IsPlaying && remaining < 0.5;
        if (_next is { IsLoaded: true, IsLoading: false } && (AutomixMath.ShouldStartFade(remaining, FadeSeconds) || ended))
            StartFade();
        else if (ended && _next is null && !_preparing && Queue.Count == 0)
            IsOn = false; // fin de la cola
    }

    private async Task PrepareNextAsync()
    {
        if (_current is not { } current || Queue.Count == 0)
            return;
        _preparing = true;
        var other = current == _deckA ? _deckB : _deckA;
        var item = Queue[0];
        Queue.RemoveAt(0);
        try
        {
            other.PauseNow();
            await other.LoadTrackAsync(_source, _source.CreateTrackInfo(item.Id));
            if (other.IsLoaded)
            {
                other.SeekTo(AutomixMath.StartPosition(other.Grid));
                _next = other;
            }
        }
        finally
        {
            _preparing = false;
        }
    }

    private void StartFade()
    {
        if (_current is not { } current || _next is not { } next)
            return;

        // Al tempo de la que suena, si cabe en el rango de pitch.
        if (_settings.AutomixMatchTempo && current.Grid is { } a && next.Grid is { } b)
        {
            double currentBpm = PitchMath.EffectiveBpm(a.Bpm, current.PitchPercent);
            if (AutomixMath.MatchPitch(currentBpm, b.Bpm, PitchMath.WideRange) is { } pitch)
            {
                if (Math.Abs(pitch) > next.PitchRange)
                    next.PitchRange = PitchMath.WideRange;
                next.PitchPercent = pitch;
            }
        }
        next.SeekTo(AutomixMath.StartPosition(next.Grid));
        next.PlayNow();
        // En fase con la que suena (como pulsar SYNC con las dos sonando).
        if (_settings.AutomixMatchTempo && current.IsPlaying && next.SyncCommand.CanExecute(null))
            next.SyncCommand.Execute(null);

        _towardsB = next == _deckB;
        _fadeStart = DateTime.Now;
        _phase = Phase.Fading;
    }
}
