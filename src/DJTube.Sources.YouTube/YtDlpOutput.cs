using System.Globalization;
using System.Text.Json;
using DJTube.Core.Logic;

namespace DJTube.Sources.YouTube;

/// <summary>
/// Argumentos para yt-dlp y lectura de las líneas que escribe.
/// </summary>
public static class YtDlpOutput
{
    public const string ProgressPrefix = "DJTUBE-PROGRESS ";
    public const string MetaPrefix = "DJTUBE-META ";

    /// <summary>Solo audio AAC en contenedor M4A (BASS lo decodifica sin plugins ni conversión).</summary>
    public const string AudioFormat = "bestaudio[ext=m4a]/140";

    public static IReadOnlyList<string> DownloadArguments(string url, string outputTemplate) =>
    [
        "--no-playlist",
        "--encoding", "utf-8",
        "--no-mtime",
        "--color", "no_color",
        "-f", AudioFormat,
        "-o", outputTemplate,
        "--progress",
        "--newline",
        "--progress-template",
        $"download:{ProgressPrefix}%(progress.downloaded_bytes)s %(progress.total_bytes)s %(progress.total_bytes_estimate)s",
        "--print",
        $"after_move:{MetaPrefix}%(.{{id,title,uploader,channel,artist,track,duration}})j",
        "--no-simulate",
        url,
    ];

    /// <summary>Línea de progreso → fracción 0..1 (null si no es de progreso o no se conoce el total).</summary>
    public static double? TryParseProgress(string line)
    {
        if (!line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
            return null;

        string[] parts = line[ProgressPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !TryParseNumber(parts[0], out double downloaded))
            return null;

        double total = 0;
        if (!TryParseNumber(parts[1], out total) && parts.Length > 2)
            TryParseNumber(parts[2], out total);
        return total > 0 ? Math.Clamp(downloaded / total, 0, 1) : null;
    }

    /// <summary>Línea de metadatos → datos de la pista (null si no lo es o no se puede leer).</summary>
    public static YouTubeDownloadInfo? TryParseMeta(string line)
    {
        if (!line.StartsWith(MetaPrefix, StringComparison.Ordinal))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(line[MetaPrefix.Length..]);
            var root = doc.RootElement;
            string? id = GetString(root, "id");
            if (id is null)
                return null;

            string? track = GetString(root, "track");
            string? artist = GetString(root, "artist");
            string title = GetString(root, "title") ?? id;
            string? channel = GetString(root, "channel") ?? GetString(root, "uploader");

            // Vídeos de YouTube Music: "track" y "artist" vienen separados.
            if (track is not null && artist is not null)
                return new YouTubeDownloadInfo(id, track, artist, GetNumber(root, "duration"), title);

            // Resto: "Artista - Canción (Official Video)" → artista y canción; un canal "Artista - Topic"
            // también da el artista.
            var (parsedArtist, parsedTitle) = TrackTitleParser.Parse(title, channel);
            return new YouTubeDownloadInfo(id, parsedTitle, artist ?? parsedArtist, GetNumber(root, "duration"), title);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"ERROR: [youtube] xxx: Video unavailable" → "[youtube] xxx: Video unavailable".</summary>
    public static string? TryParseError(string line) =>
        line.StartsWith("ERROR:", StringComparison.Ordinal) ? line["ERROR:".Length..].Trim() : null;

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } s
            ? s
            : null;

    private static double? GetNumber(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
}
