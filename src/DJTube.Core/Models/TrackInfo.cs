namespace DJTube.Core.Models;

/// <summary>
/// Metadatos de una pista tal como los expone una <see cref="Abstractions.ITrackSource"/>.
/// <paramref name="Id"/> es opaco para el motor: solo lo interpreta la fuente que lo creó.
/// </summary>
public sealed record TrackInfo(
    string SourceName,
    string Id,
    string Title,
    string? Artist = null,
    TimeSpan? Duration = null);
