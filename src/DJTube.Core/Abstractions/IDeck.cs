using DJTube.Core.Models;

namespace DJTube.Core.Abstractions;

public interface IDeck : IDeckTransport
{
    TrackInfo? Track { get; }

    bool IsLoaded { get; }

    double DurationSeconds { get; }

    /// <summary>La pista ha llegado al final.</summary>
    bool HasEnded { get; }

    /// <summary>Cambio de velocidad en % (+8 = 8 % más rápido). Se conserva al cargar otra pista.</summary>
    double PitchPercent { get; set; }

    /// <summary>Si está activo, el pitch cambia el tempo sin cambiar el tono.</summary>
    bool KeyLock { get; set; }

    /// <summary>Ganancia hacia el master (fader de canal × crossfader). 1 = sin cambio.</summary>
    double MasterGain { get; set; }

    /// <summary>Pre-escucha: el plato suena por los auriculares (independiente de faders y crossfader).</summary>
    bool CueEnabled { get; set; }

    /// <summary>
    /// Prepara la pista en segundo plano y la sustituye por la actual cuando está lista.
    /// El plato queda en pausa al principio. Si se inicia otra carga antes de terminar,
    /// esta se descarta con <see cref="OperationCanceledException"/>.
    /// </summary>
    Task LoadAsync(TrackMedia media, CancellationToken ct = default);

    void Unload();
}
