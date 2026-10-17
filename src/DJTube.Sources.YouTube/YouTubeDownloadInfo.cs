namespace DJTube.Sources.YouTube;

/// <summary>Datos de una pista descargada (se guardan junto al audio en la caché).</summary>
/// <param name="Title">Título de la canción (limpio, sin "Official Video" y similares).</param>
/// <param name="Artist">Artista, si se conoce.</param>
/// <param name="OriginalTitle">Título del vídeo tal cual (null en cachés antiguas: se rellena al leerlas).</param>
public sealed record YouTubeDownloadInfo(
    string Id, string Title, string? Artist, double? DurationSeconds, string? OriginalTitle = null);

public class YouTubeSourceException : Exception
{
    public YouTubeSourceException(string message)
        : base(message)
    {
    }
}
