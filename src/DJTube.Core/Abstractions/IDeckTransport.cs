namespace DJTube.Core.Abstractions;

/// <summary>
/// Controles de transporte de un plato. Todas las operaciones son rápidas y no bloquean:
/// se pueden llamar desde el hilo de la interfaz.
/// </summary>
public interface IDeckTransport
{
    bool IsPlaying { get; }

    /// <summary>Posición que se está oyendo, en segundos (compensando la latencia de salida).</summary>
    double PositionSeconds { get; }

    void Play();

    void Pause();

    void Seek(double seconds);
}
