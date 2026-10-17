using DJTube.Core.Models;

namespace DJTube.Core.Abstractions;

public interface IDeck : IDeckTransport
{
    TrackInfo? Track { get; }

    bool IsLoaded { get; }

    double DurationSeconds { get; }

    /// <summary>La pista ha llegado al final.</summary>
    bool HasEnded { get; }

    /// <summary>
    /// Prepara la pista en segundo plano y la sustituye por la actual cuando está lista.
    /// El plato queda en pausa al principio. Si se inicia otra carga antes de terminar,
    /// esta se descarta con <see cref="OperationCanceledException"/>.
    /// </summary>
    Task LoadAsync(TrackMedia media, CancellationToken ct = default);

    void Unload();
}
