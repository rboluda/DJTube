using DJTube.Core.Recording;

namespace DJTube.Core.Tests;

public sealed class SessionHistoryTests
{
    private static readonly DateTime T0 = new(2026, 10, 18, 22, 0, 0);

    private static DeckSnapshot Deck(string deck, string? key, bool playing = true, bool audible = true) =>
        new(deck, key, key is null ? null : "Artista " + key, key is null ? null : "Tema " + key,
            key is null ? null : "https://www.youtube.com/watch?v=" + key, playing, audible);

    [Fact]
    public void SongIsLoggedAfterTenAudibleSeconds()
    {
        var history = new SessionHistory(() => null);
        history.Update(T0, [Deck("A", "x")]);
        history.Update(T0.AddSeconds(9), [Deck("A", "x")]);
        Assert.Empty(history.Entries);

        history.Update(T0.AddSeconds(10), [Deck("A", "x")]);
        var entry = Assert.Single(history.Entries);
        Assert.Equal(T0, entry.Time); // la hora en que empezó a oírse
        Assert.Equal("Artista x — Tema x", entry.Display);
    }

    [Fact]
    public void PreListeningWithTheFaderDownDoesNotCount()
    {
        var history = new SessionHistory(() => null);
        for (int s = 0; s <= 30; s += 5)
            history.Update(T0.AddSeconds(s), [Deck("A", "x", audible: false)]);
        Assert.Empty(history.Entries);
    }

    [Fact]
    public void SameLoadIsLoggedOnceEvenAfterPausing()
    {
        var history = new SessionHistory(() => null);
        history.Update(T0, [Deck("A", "x")]);
        history.Update(T0.AddSeconds(11), [Deck("A", "x")]);
        history.Update(T0.AddSeconds(12), [Deck("A", "x", playing: false)]);
        history.Update(T0.AddSeconds(13), [Deck("A", "x")]);
        history.Update(T0.AddSeconds(30), [Deck("A", "x")]);
        Assert.Single(history.Entries);

        // Otra canción en el plato sí cuenta.
        history.Update(T0.AddSeconds(40), [Deck("A", "y")]);
        history.Update(T0.AddSeconds(51), [Deck("A", "y")]);
        Assert.Equal(2, history.Entries.Count);
    }

    [Fact]
    public void RecordingOffsetsAndCueSheet()
    {
        TimeSpan? recording = null;
        var history = new SessionHistory(() => recording);

        // A ya suena cuando empieza la grabación.
        history.Update(T0, [Deck("A", "x"), Deck("B", null)]);
        history.Update(T0.AddSeconds(15), [Deck("A", "x"), Deck("B", null)]);
        recording = TimeSpan.Zero;
        history.OnRecordingStarted();

        // B entra a los 2:00 de grabación y lleva 10 s sonando a los 2:10.
        recording = TimeSpan.FromSeconds(130);
        history.Update(T0.AddSeconds(135), [Deck("A", "x"), Deck("B", "y")]);
        recording = TimeSpan.FromSeconds(140);
        history.Update(T0.AddSeconds(145), [Deck("A", "x"), Deck("B", "y")]);

        Assert.Equal(2, history.RecordingEntries.Count);
        Assert.Equal(TimeSpan.Zero, history.RecordingEntries[0].RecordingOffset);
        Assert.Equal(TimeSpan.FromSeconds(130), history.RecordingEntries[1].RecordingOffset);

        string text = SessionHistory.ToText(history.RecordingEntries, useRecordingTime: true);
        Assert.Contains("00:00:00  Artista x — Tema x", text);
        Assert.Contains("00:02:10  Artista y — Tema y", text);

        string cue = SessionHistory.ToCueSheet("sesion.wav", "Sesión", history.RecordingEntries);
        Assert.Contains("FILE \"sesion.wav\" WAVE", cue);
        Assert.Contains("TRACK 02 AUDIO", cue);
        Assert.Contains("INDEX 01 02:10:00", cue);
        Assert.Contains("PERFORMER \"Artista y\"", cue);
    }
}
