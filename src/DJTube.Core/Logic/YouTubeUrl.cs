using System.Text.RegularExpressions;

namespace DJTube.Core.Logic;

/// <summary>
/// Reconoce enlaces de vídeos de YouTube y extrae el ID (11 caracteres).
/// </summary>
public static partial class YouTubeUrl
{
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoIdPattern();

    private static readonly string[] WatchHosts =
        ["youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com"];

    public static bool IsValidVideoId(string? id) => id is not null && VideoIdPattern().IsMatch(id);

    /// <summary>
    /// Admite youtube.com/watch?v=, youtu.be/, music.youtube.com, /shorts/, /live/ y /embed/.
    /// En enlaces con lista (&amp;list=) se queda solo con el vídeo.
    /// </summary>
    public static bool TryGetVideoId(string? text, out string videoId)
    {
        videoId = "";
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string candidate = text.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
            candidate = "https://" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return false;

        string host = uri.Host.ToLowerInvariant();
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? id = null;

        if (host is "youtu.be" or "www.youtu.be")
        {
            id = segments.FirstOrDefault();
        }
        else if (WatchHosts.Contains(host))
        {
            if (segments is ["watch"])
                id = GetQueryValue(uri.Query, "v");
            else if (segments is ["shorts" or "live" or "embed" or "v", var pathId, ..])
                id = pathId;
        }

        if (!IsValidVideoId(id))
            return false;

        videoId = id!;
        return true;
    }

    /// <summary>Enlace canónico para un ID.</summary>
    public static string ToWatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    [GeneratedRegex("^[A-Za-z0-9_-]{2,80}$")]
    private static partial Regex PlaylistIdPattern();

    /// <summary>
    /// ID de la lista de un enlace de YouTube: youtube.com/playlist?list=… o un vídeo dentro de una
    /// lista (watch?v=…&amp;list=…, también los «Mix» automáticos, que empiezan por RD).
    /// </summary>
    public static bool TryGetPlaylistId(string? text, out string playlistId)
    {
        playlistId = "";
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string candidate = text.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
            candidate = "https://" + candidate;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !WatchHosts.Contains(uri.Host.ToLowerInvariant()))
            return false;

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is not (["playlist"] or ["watch"]))
            return false;
        string? id = GetQueryValue(uri.Query, "list");
        if (id is null || !PlaylistIdPattern().IsMatch(id))
            return false;
        playlistId = id;
        return true;
    }

    /// <summary>
    /// Enlace para pedir la lista entera. Los «Mix» (RD…) solo existen a partir de un vídeo, así que
    /// conservan el vídeo de partida.
    /// </summary>
    public static string ToPlaylistUrl(string playlistId, string? videoId = null) =>
        playlistId.StartsWith("RD", StringComparison.Ordinal) && IsValidVideoId(videoId)
            ? $"https://www.youtube.com/watch?v={videoId}&list={playlistId}"
            : $"https://www.youtube.com/playlist?list={playlistId}";

    private static string? GetQueryValue(string query, string key)
    {
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && pair[..eq] == key)
                return Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return null;
    }
}
