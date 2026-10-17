using System.Globalization;
using System.Text;

namespace DJTube.Core.Recording;

/// <summary>Una canción que ha sonado en la sesión.</summary>
public sealed record HistoryEntry(DateTime Time, string Deck, string? Artist, string Title, string? Link)
{
    /// <summary>Momento de la grabación en el que empezó a sonar (null si no se estaba grabando).</summary>
    public TimeSpan? RecordingOffset { get; init; }

    public string Display => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} — {Title}";
}

/// <summary>Estado de un plato en un instante, para el historial.</summary>
public readonly record struct DeckSnapshot(string Deck, string? TrackKey, string? Artist, string? Title, string? Link, bool IsPlaying, bool IsAudible);

/// <summary>
/// Historial de la sesión: apunta una canción cuando lleva <see cref="MinAudibleSeconds"/> sonando por el
/// master (reproduciendo con el fader y el crossfader abiertos), una vez por carga. Sirve para la lista de
/// temas que se guarda junto a la grabación.
/// </summary>
public sealed class SessionHistory
{
    public const double MinAudibleSeconds = 10;

    private readonly Dictionary<string, DeckState> _decks = [];
    private readonly List<HistoryEntry> _entries = [];
    private readonly Func<TimeSpan?> _recordingPosition;

    private sealed class DeckState
    {
        public string? TrackKey;
        public DateTime? AudibleSince;
        public bool Logged;
        public HistoryEntry? Entry;
    }

    /// <param name="recordingPosition">Posición de la grabación en curso (null si no se graba).</param>
    public SessionHistory(Func<TimeSpan?> recordingPosition)
    {
        _recordingPosition = recordingPosition;
    }

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    /// <summary>Canciones de la grabación actual (o la última), con su momento dentro de ella.</summary>
    public IReadOnlyList<HistoryEntry> RecordingEntries => _recording;

    private readonly List<HistoryEntry> _recording = [];

    public event EventHandler<HistoryEntry>? EntryAdded;

    /// <summary>Se llama periódicamente con el estado de los platos.</summary>
    public void Update(DateTime now, IEnumerable<DeckSnapshot> decks)
    {
        foreach (var deck in decks)
        {
            if (!_decks.TryGetValue(deck.Deck, out var state))
                _decks[deck.Deck] = state = new DeckState();

            if (deck.TrackKey != state.TrackKey)
            {
                // Otra canción en el plato: se vuelve a contar.
                state.TrackKey = deck.TrackKey;
                state.AudibleSince = null;
                state.Logged = false;
                state.Entry = null;
            }
            if (deck.TrackKey is null || deck.Title is null)
                continue;

            if (!(deck.IsPlaying && deck.IsAudible))
            {
                state.AudibleSince = null;
                continue;
            }
            state.AudibleSince ??= now;
            if (state.Logged || (now - state.AudibleSince.Value).TotalSeconds < MinAudibleSeconds)
                continue;

            state.Logged = true;
            var entry = new HistoryEntry(state.AudibleSince.Value, deck.Deck, deck.Artist, deck.Title, deck.Link);
            if (_recordingPosition() is { } position)
            {
                var offset = position - (now - state.AudibleSince.Value);
                entry = entry with { RecordingOffset = offset < TimeSpan.Zero ? TimeSpan.Zero : offset };
                _recording.Add(entry);
            }
            state.Entry = entry;
            _entries.Add(entry);
            EntryAdded?.Invoke(this, entry);
        }
    }

    /// <summary>Empieza una grabación: lo que ya está sonando entra al principio de su lista.</summary>
    public void OnRecordingStarted()
    {
        _recording.Clear();
        foreach (var state in _decks.Values)
        {
            if (state.Entry is { } entry && state.AudibleSince is not null)
                _recording.Add(entry with { RecordingOffset = TimeSpan.Zero });
        }
    }

    /// <summary>Lista de temas en texto: "00:12:34  Artista — Título".</summary>
    public static string ToText(IEnumerable<HistoryEntry> entries, bool useRecordingTime)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            string time = useRecordingTime && e.RecordingOffset is { } o
                ? $"{(int)o.TotalHours:00}:{o.Minutes:00}:{o.Seconds:00}"
                : e.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            sb.Append(time).Append("  ").Append(e.Display);
            if (e.Link is { } link)
                sb.Append("  ").Append(link);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Hoja CUE para la grabación: los reproductores y editores la usan para dividir la sesión en temas.
    /// </summary>
    public static string ToCueSheet(string audioFileName, string title, IEnumerable<HistoryEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TITLE \"{Escape(title)}\"");
        sb.AppendLine("PERFORMER \"DJTube\"");
        sb.AppendLine($"FILE \"{Escape(audioFileName)}\" WAVE");
        int n = 1;
        foreach (var e in entries.Where(e => e.RecordingOffset is not null).OrderBy(e => e.RecordingOffset))
        {
            var o = e.RecordingOffset!.Value;
            int frames = (int)Math.Round(o.TotalSeconds * 75);
            sb.AppendLine(FormattableString.Invariant($"  TRACK {n++:00} AUDIO"));
            sb.AppendLine($"    TITLE \"{Escape(e.Title)}\"");
            if (!string.IsNullOrWhiteSpace(e.Artist))
                sb.AppendLine($"    PERFORMER \"{Escape(e.Artist)}\"");
            sb.AppendLine(FormattableString.Invariant($"    INDEX 01 {frames / 75 / 60:00}:{frames / 75 % 60:00}:{frames % 75:00}"));
        }
        return sb.ToString();
    }

    private static string Escape(string text) => text.Replace('"', '\'');
}
