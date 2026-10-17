namespace DJTube.Core.Models;

/// <summary>
/// Datos de audio listos para que el motor los decodifique: o bien una ruta a un archivo,
/// o bien un <see cref="System.IO.Stream"/> legible y con búsqueda (seek).
/// </summary>
public sealed class TrackMedia : IDisposable
{
    private TrackMedia(TrackInfo track, string? filePath, Stream? stream)
    {
        Track = track;
        FilePath = filePath;
        Stream = stream;
    }

    public TrackInfo Track { get; }

    public string? FilePath { get; }

    public Stream? Stream { get; }

    public bool IsFile => FilePath is not null;

    public static TrackMedia FromFile(TrackInfo track, string filePath)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return new TrackMedia(track, filePath, null);
    }

    public static TrackMedia FromStream(TrackInfo track, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("El stream debe admitir lectura y búsqueda.", nameof(stream));
        return new TrackMedia(track, null, stream);
    }

    public void Dispose() => Stream?.Dispose();
}
