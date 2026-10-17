using DJTube.Core.Models;

namespace DJTube.Core.Abstractions;

/// <summary>
/// Origen de pistas desacoplado del motor de audio (archivos locales, YouTube, etc.).
/// </summary>
public interface ITrackSource
{
    string Name { get; }

    /// <summary>Enumera las pistas disponibles en <paramref name="location"/> (o en la raíz si es null).</summary>
    IAsyncEnumerable<TrackInfo> BrowseAsync(string? location, CancellationToken ct = default);

    /// <summary>
    /// Deja la pista lista para decodificar (descargándola si hace falta). El llamador es dueño del resultado.
    /// El <see cref="TrackMedia.Track"/> devuelto puede traer más datos que <paramref name="track"/> (título, duración…).
    /// </summary>
    /// <param name="progress">Progreso de la preparación (0..1), si la fuente lo conoce.</param>
    Task<TrackMedia> OpenAsync(TrackInfo track, IProgress<double>? progress = null, CancellationToken ct = default);
}
