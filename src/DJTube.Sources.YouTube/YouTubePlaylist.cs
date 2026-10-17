namespace DJTube.Sources.YouTube;

/// <summary>Un vídeo de una lista de YouTube (aún sin descargar).</summary>
public sealed record YouTubePlaylistEntry(string Id, string Title, string? Artist, double? DurationSeconds, string? Channel = null, string? OriginalTitle = null)
{
    public string ThumbnailUrl => $"https://i.ytimg.com/vi/{Id}/mqdefault.jpg";
}

/// <summary>Contenido de una lista de reproducción de YouTube.</summary>
public sealed record YouTubePlaylist(string Title, IReadOnlyList<YouTubePlaylistEntry> Entries);
