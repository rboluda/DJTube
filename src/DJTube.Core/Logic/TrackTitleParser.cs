using System.Text.RegularExpressions;

namespace DJTube.Core.Logic;

/// <summary>
/// Saca artista y título de un título de vídeo de YouTube: "Artista - Canción (Official Video) [HD]"
/// → ("Artista", "Canción"). Quita las coletillas habituales (vídeo oficial, letra, calidad…) y
/// conserva lo que forma parte de la canción ("ft.", "Remix", "Original Mix"…).
/// </summary>
public static partial class TrackTitleParser
{
    // Separadores entre artista y canción: guion, guion largo, raya, dos puntos o barra vertical con espacios.
    [GeneratedRegex(@"\s+[-–—|:]\s+|\s+[-–—]|[-–—]\s+")]
    private static partial Regex Separator();

    // Coletillas entre paréntesis o corchetes que no son parte del título.
    [GeneratedRegex(
        @"\s*[\(\[]\s*(?:official|offical|oficial|video\s*oficial|videoclip|lyric|lyrics|letra|audio|visualizer|visualiser|" +
        @"music\s*video|full\s*hd|hd|hq|4k|8k|1080p|720p|explicit|clean|remastered\s*\d*|\d+\s*kbps|free\s*download|" +
        @"out\s*now|premiere|estreno|en\s*vivo\s*oficial|clip\s*officiel|M/?V)[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    // Restos sueltos al final: "| Official Video", "- Lyrics", "HD".
    [GeneratedRegex(@"\s*(?:\||//)\s*(?:official|oficial|lyric|lyrics|letra|video|audio)[^|]*$|\s+(?:HD|HQ|4K)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TrailingNoise();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    /// <param name="videoTitle">Título del vídeo.</param>
    /// <param name="channel">Canal (si es "Artista - Topic" se usa como artista).</param>
    /// <returns>Artista (o null si no se puede saber) y título limpio.</returns>
    public static (string? Artist, string Title) Parse(string videoTitle, string? channel = null)
    {
        string clean = Clean(videoTitle ?? "");
        string? topicArtist = TopicArtist(channel);

        var match = Separator().Match(clean);
        if (match.Success && match.Index > 0 && match.Index + match.Length < clean.Length)
        {
            string artist = Trim(clean[..match.Index]);
            string title = clean[(match.Index + match.Length)..];
            // Tras el título, lo que va detrás de " | " suele ser el álbum o el canal.
            int bar = title.IndexOf(" | ", StringComparison.Ordinal);
            if (bar > 0)
                title = title[..bar];
            title = Trim(title);
            if (artist.Length > 0 && title.Length > 0)
                return (artist, title);
        }

        return (topicArtist, clean.Length > 0 ? clean : (videoTitle ?? "").Trim());
    }

    /// <summary>"Artista - Canción" o solo la canción si no hay artista.</summary>
    public static string Display(string? artist, string title) =>
        string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";

    private static string Clean(string text)
    {
        string s = text.Trim();
        // Repetir: a veces hay varias coletillas seguidas ("(Official Video) [HD]").
        for (int i = 0; i < 3; i++)
        {
            string before = s;
            s = Noise().Replace(s, "");
            s = TrailingNoise().Replace(s, "");
            if (s == before)
                break;
        }
        s = s.Replace('"', ' ').Replace('“', ' ').Replace('”', ' ');
        return Trim(Spaces().Replace(s, " "));
    }

    private static string? TopicArtist(string? channel)
    {
        const string suffix = " - Topic";
        return channel is not null && channel.EndsWith(suffix, StringComparison.Ordinal) ? channel[..^suffix.Length].Trim() : null;
    }

    private static string Trim(string s) => s.Trim(' ', '-', '–', '—', '|', ':', '·', ',');
}
