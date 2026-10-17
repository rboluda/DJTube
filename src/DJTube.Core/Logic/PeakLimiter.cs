namespace DJTube.Core.Logic;

/// <summary>
/// Limitador de picos con anticipación ("brickwall"): ninguna muestra supera el techo. Retrasa la señal unos
/// milisegundos para bajar la ganancia justo antes de que llegue el pico (sin distorsión audible) y la
/// recupera despacio. Pensado para el hilo de audio: no reserva memoria ni bloquea después de crearse.
/// </summary>
public sealed class PeakLimiter
{
    private readonly int _channels;
    private readonly int _lookahead;
    private readonly float[] _delay;       // muestras retrasadas (lookahead × canales), en anillo
    private readonly float[] _required;    // ganancia necesaria de cada fotograma de la ventana
    private readonly double _release;
    private int _index;
    private double _gain = 1;

    /// <param name="ceiling">Techo lineal (0,966 ≈ −0,3 dBFS).</param>
    public PeakLimiter(int sampleRate, int channels, double ceiling = 0.966, double lookaheadMs = 1.5, double releaseMs = 60)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        _channels = channels;
        Ceiling = (float)Math.Clamp(ceiling, 0.1, 1);
        _lookahead = Math.Max(1, (int)Math.Round(sampleRate * lookaheadMs / 1000));
        _delay = new float[_lookahead * channels];
        _required = new float[_lookahead];
        Array.Fill(_required, 1f);
        _release = 1 - Math.Exp(-1 / (sampleRate * releaseMs / 1000));
    }

    public float Ceiling { get; }

    /// <summary>Fotogramas de retraso que añade.</summary>
    public int LatencyFrames => _lookahead;

    /// <summary>Reducción de ganancia actual (1 = sin reducir), para un indicador.</summary>
    public double CurrentGain => _gain;

    /// <summary>Procesa en el sitio muestras intercaladas.</summary>
    public void Process(Span<float> interleaved)
    {
        int frames = interleaved.Length / _channels;
        for (int f = 0; f < frames; f++)
        {
            int offset = f * _channels;

            // Ganancia que necesita el fotograma que entra.
            float peak = 0;
            for (int c = 0; c < _channels; c++)
                peak = Math.Max(peak, Math.Abs(interleaved[offset + c]));
            _required[_index] = peak > Ceiling ? Ceiling / peak : 1f;

            // La menor de la ventana: así la ganancia ya ha bajado cuando el pico sale del retraso.
            float target = 1f;
            for (int i = 0; i < _lookahead; i++)
                target = Math.Min(target, _required[i]);

            _gain = target < _gain ? target : _gain + (target - _gain) * _release;

            // Sale el fotograma retrasado y entra el nuevo.
            int slot = _index * _channels;
            for (int c = 0; c < _channels; c++)
            {
                float delayed = _delay[slot + c];
                _delay[slot + c] = interleaved[offset + c];
                float y = (float)(delayed * _gain);
                interleaved[offset + c] = Math.Clamp(y, -Ceiling, Ceiling); // red de seguridad
            }
            _index = (_index + 1) % _lookahead;
        }
    }

    /// <summary>Vacía el retraso (al reiniciar la salida).</summary>
    public void Reset()
    {
        Array.Clear(_delay);
        Array.Fill(_required, 1f);
        _gain = 1;
        _index = 0;
    }
}
