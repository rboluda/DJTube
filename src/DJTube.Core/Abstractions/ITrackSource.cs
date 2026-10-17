using DJTube.Core.Models;

namespace DJTube.Core.Abstractions;

/// <summary>
/// Origen de pistas desacoplado del motor de audio (archivos locales, etc.).
/// </summary>
public interface ITrackSource
{
    string Name { get; }

    /// <summary>Enumera las pistas disponibles en <paramref name="location"/> (o en la raíz si es null).</summary>
    IAsyncEnumerable<TrackInfo> BrowseAsync(string? location, CancellationToken ct = default);

    /// <summary>Abre la pista para su decodificación. El llamador es dueño del resultado.</summary>
    Task<TrackMedia> OpenAsync(TrackInfo track, CancellationToken ct = default);
}
