using System.Text.Json;

namespace DJTube.Core.Midi;

public enum MidiSignalKind
{
    Note,
    ControlChange,
    PitchBend,
}

/// <summary>Qué control de la controladora: tipo, canal (0–15) y número de nota o de CC.</summary>
public readonly record struct MidiSignal(MidiSignalKind Kind, int Channel, int Number)
{
    public override string ToString() => Kind switch
    {
        MidiSignalKind.Note => $"Note {Number} · ch {Channel + 1}",
        MidiSignalKind.ControlChange => $"CC {Number} · ch {Channel + 1}",
        _ => $"Pitch bend · ch {Channel + 1}",
    };
}

/// <summary>Un mensaje MIDI de entrada ya interpretado.</summary>
public readonly record struct MidiMessage(MidiSignal Signal, int Value, int MaxValue)
{
    /// <summary>Valor normalizado 0..1 (fader, mando).</summary>
    public double Normalized => MaxValue > 0 ? Math.Clamp(Value / (double)MaxValue, 0, 1) : 0;

    /// <summary>Botón pulsado (nota con velocidad, o CC distinto de 0).</summary>
    public bool IsPress => Value > 0;

    /// <summary>
    /// Giro de un control relativo (jog, encoder): valores 1–63 hacia delante y 65–127 hacia atrás
    /// (complemento a dos, lo habitual en controladoras DJ).
    /// </summary>
    public int RelativeDelta => Signal.Kind == MidiSignalKind.ControlChange ? (Value < 64 ? Value : Value - 128) : 0;

    /// <summary>
    /// Interpreta un mensaje corto de winmm (status | data1 &lt;&lt; 8 | data2 &lt;&lt; 16). null si no es de
    /// nota, CC o pitch bend.
    /// </summary>
    public static MidiMessage? Parse(int shortMessage)
    {
        int status = shortMessage & 0xFF, data1 = (shortMessage >> 8) & 0x7F, data2 = (shortMessage >> 16) & 0x7F;
        int channel = status & 0x0F;
        return (status & 0xF0) switch
        {
            0x90 => new MidiMessage(new MidiSignal(MidiSignalKind.Note, channel, data1), data2, 127),
            0x80 => new MidiMessage(new MidiSignal(MidiSignalKind.Note, channel, data1), 0, 127),
            0xB0 => new MidiMessage(new MidiSignal(MidiSignalKind.ControlChange, channel, data1), data2, 127),
            0xE0 => new MidiMessage(new MidiSignal(MidiSignalKind.PitchBend, channel, 0), data1 | (data2 << 7), 16383),
            _ => null,
        };
    }
}

/// <summary>Cómo se usa una acción: botón, valor absoluto (fader/mando) o relativo (jog).</summary>
public enum MidiActionKind
{
    Button,
    Absolute,
    Relative,
}

/// <summary>Una acción de DJTube que se puede asignar a un control.</summary>
public sealed record MidiAction(string Id, MidiActionKind Kind);

/// <summary>
/// Asignaciones de controles MIDI a acciones de DJTube, con modo "aprender": se elige la acción, se mueve
/// el control de la controladora y quedan unidos. Sirve para cualquier marca.
/// </summary>
public sealed class MidiMapping
{
    private readonly Dictionary<MidiSignal, string> _bySignal = [];

    /// <summary>Acción esperando a que se mueva un control (modo aprender), o null.</summary>
    public string? Learning { get; private set; }

    public IReadOnlyDictionary<MidiSignal, string> Bindings => _bySignal;

    /// <summary>Se ha asignado un control en modo aprender.</summary>
    public event EventHandler<(string Action, MidiSignal Signal)>? Learned;

    public void StartLearning(string actionId) => Learning = actionId;

    public void CancelLearning() => Learning = null;

    public MidiSignal? SignalFor(string actionId) =>
        _bySignal.FirstOrDefault(kv => kv.Value == actionId) is { Value: not null } kv ? kv.Key : null;

    public void Bind(MidiSignal signal, string actionId)
    {
        Unbind(actionId);
        _bySignal[signal] = actionId;
    }

    public void Unbind(string actionId)
    {
        foreach (var signal in _bySignal.Where(kv => kv.Value == actionId).Select(kv => kv.Key).ToList())
            _bySignal.Remove(signal);
    }

    public void Clear() => _bySignal.Clear();

    /// <summary>
    /// Procesa un mensaje: en modo aprender lo asigna (y no lo ejecuta); si no, devuelve la acción asignada.
    /// Los "note off" no sirven para aprender (al soltar un botón se aprendería otra vez el mismo).
    /// </summary>
    public string? Handle(MidiMessage message)
    {
        if (Learning is { } action)
        {
            if (message.Signal.Kind == MidiSignalKind.Note && !message.IsPress)
                return null;
            Bind(message.Signal, action);
            Learning = null;
            Learned?.Invoke(this, (action, message.Signal));
            return null;
        }
        return _bySignal.TryGetValue(message.Signal, out var bound) ? bound : null;
    }

    // ---------- Guardar ----------

    private sealed record Saved(MidiSignalKind Kind, int Channel, int Number, string Action);

    public string ToJson() => JsonSerializer.Serialize(
        _bySignal.Select(kv => new Saved(kv.Key.Kind, kv.Key.Channel, kv.Key.Number, kv.Value)).ToList());

    public static MidiMapping FromJson(string? json)
    {
        var mapping = new MidiMapping();
        if (string.IsNullOrWhiteSpace(json))
            return mapping;
        try
        {
            foreach (var s in JsonSerializer.Deserialize<List<Saved>>(json) ?? [])
            {
                if (s.Action is { Length: > 0 })
                    mapping._bySignal[new MidiSignal(s.Kind, s.Channel, s.Number)] = s.Action;
            }
        }
        catch (JsonException)
        {
        }
        return mapping;
    }
}
