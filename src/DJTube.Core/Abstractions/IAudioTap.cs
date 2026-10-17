namespace DJTube.Core.Abstractions;

/// <summary>Bloque leído de un <see cref="IAudioTap"/>: muestras float intercaladas y su formato.</summary>
public readonly record struct TapRead(int Samples, int SampleRate, int Channels);

/// <summary>
/// Copia exacta de lo que suena por el master, para grabarlo. Se lee desde un hilo propio
/// (nunca desde el de audio); lo que no se lea en unos segundos se pierde.
/// </summary>
public interface IAudioTap : IDisposable
{
    /// <summary>
    /// Copia en <paramref name="buffer"/> lo que ha sonado desde la última lectura (como mucho lo que
    /// quepa). Samples = 0 si no hay nada nuevo. El formato puede cambiar si cambia la salida.
    /// </summary>
    TapRead Read(float[] buffer);
}
