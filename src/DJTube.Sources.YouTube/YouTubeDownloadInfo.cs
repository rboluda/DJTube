namespace DJTube.Sources.YouTube;

/// <summary>Datos de una pista descargada (se guardan junto al audio en la caché).</summary>
public sealed record YouTubeDownloadInfo(string Id, string Title, string? Artist, double? DurationSeconds);

public class YouTubeSourceException : Exception
{
    public YouTubeSourceException(string message)
        : base(message)
    {
    }
}
