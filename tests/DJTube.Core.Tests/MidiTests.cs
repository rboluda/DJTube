using DJTube.Core.Midi;

namespace DJTube.Core.Tests;

public sealed class MidiTests
{
    private static int Short(int status, int d1, int d2) => status | (d1 << 8) | (d2 << 16);

    [Fact]
    public void Parse_NoteOnOffAndCc()
    {
        var on = MidiMessage.Parse(Short(0x91, 36, 100))!.Value;
        Assert.Equal(new MidiSignal(MidiSignalKind.Note, 1, 36), on.Signal);
        Assert.True(on.IsPress);

        var off = MidiMessage.Parse(Short(0x81, 36, 64))!.Value;
        Assert.False(off.IsPress);
        Assert.False(MidiMessage.Parse(Short(0x91, 36, 0))!.Value.IsPress); // note on con velocidad 0 = soltar

        var cc = MidiMessage.Parse(Short(0xB0, 7, 127))!.Value;
        Assert.Equal(MidiSignalKind.ControlChange, cc.Signal.Kind);
        Assert.Equal(1, cc.Normalized, 6);
    }

    [Fact]
    public void Parse_PitchBendIsFourteenBits()
    {
        var bend = MidiMessage.Parse(Short(0xE2, 0x7F, 0x7F))!.Value;
        Assert.Equal(MidiSignalKind.PitchBend, bend.Signal.Kind);
        Assert.Equal(16383, bend.Value);
        Assert.Equal(1, bend.Normalized, 6);
        Assert.Equal(0.5, MidiMessage.Parse(Short(0xE0, 0, 64))!.Value.Normalized, 3);
    }

    [Fact]
    public void Parse_IgnoresOtherMessages()
    {
        Assert.Null(MidiMessage.Parse(0xF8)); // reloj
        Assert.Null(MidiMessage.Parse(Short(0xC0, 5, 0))); // cambio de programa
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(127, -1)]
    [InlineData(125, -3)]
    public void Relative_TwosComplement(int value, int delta)
    {
        Assert.Equal(delta, MidiMessage.Parse(Short(0xB0, 20, value))!.Value.RelativeDelta);
    }

    [Fact]
    public void Learn_BindsTheNextControl()
    {
        var mapping = new MidiMapping();
        (string, MidiSignal)? learned = null;
        mapping.Learned += (_, e) => learned = e;

        mapping.StartLearning("DeckA.PlayPause");
        Assert.Null(mapping.Handle(MidiMessage.Parse(Short(0x80, 11, 0))!.Value)); // soltar no cuenta
        Assert.Equal("DeckA.PlayPause", mapping.Learning);
        Assert.Null(mapping.Handle(MidiMessage.Parse(Short(0x90, 11, 127))!.Value)); // se aprende, no se ejecuta

        Assert.Null(mapping.Learning);
        Assert.Equal(("DeckA.PlayPause", new MidiSignal(MidiSignalKind.Note, 0, 11)), learned);
        Assert.Equal("DeckA.PlayPause", mapping.Handle(MidiMessage.Parse(Short(0x90, 11, 127))!.Value));
        Assert.Null(mapping.Handle(MidiMessage.Parse(Short(0x90, 12, 127))!.Value));
    }

    [Fact]
    public void Bind_ReplacesTheOldControlOfTheAction()
    {
        var mapping = new MidiMapping();
        var a = new MidiSignal(MidiSignalKind.ControlChange, 0, 1);
        var b = new MidiSignal(MidiSignalKind.ControlChange, 0, 2);
        mapping.Bind(a, "Mixer.Crossfader");
        mapping.Bind(b, "Mixer.Crossfader");
        Assert.Equal(b, mapping.SignalFor("Mixer.Crossfader"));
        Assert.Single(mapping.Bindings);
    }

    [Fact]
    public void Json_RoundTrip()
    {
        var mapping = new MidiMapping();
        mapping.Bind(new MidiSignal(MidiSignalKind.Note, 2, 40), "DeckB.Cue");
        mapping.Bind(new MidiSignal(MidiSignalKind.PitchBend, 0, 0), "DeckA.Pitch");

        var loaded = MidiMapping.FromJson(mapping.ToJson());
        Assert.Equal("DeckB.Cue", loaded.Bindings[new MidiSignal(MidiSignalKind.Note, 2, 40)]);
        Assert.Equal(2, loaded.Bindings.Count);
        Assert.Empty(MidiMapping.FromJson("no es json").Bindings);
    }
}
