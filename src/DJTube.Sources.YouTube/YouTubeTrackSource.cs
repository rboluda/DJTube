using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DJTube.Core.Abstractions;
using DJTube.Core.Logic;
using DJTube.Core.Models;

namespace DJTube.Sources.YouTube;

/// <summary>
/// Pistas desde enlaces de YouTube: descarga el audio (M4A/AAC) con yt-dlp y lo guarda en una caché local.
/// El <see cref="TrackInfo.Id"/> es el ID del vídeo.
/// </summary>
/// <remarks>
/// Por cada vídeo hay dos archivos en la caché: <c>{id}.m4a</c> (audio) y <c>{id}.json</c> (datos).
/// El .json se escribe al final, así que una pista solo está en caché si existen los dos.
/// </remarks>
public sealed class YouTubeTrackSource : ITrackSource
{
    public const string SourceName = "YouTube";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Func<string?> _findExecutable;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public YouTubeTrackSource(string? cacheFolder = null, Func<string?>? findExecutable = null)
    {
        CacheFolder = cacheFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DJTube", "Cache", "YouTube");
        _findExecutable = findExecutable ?? YtDlp.Find;
    }

    public string Name => SourceName;

    public string CacheFolder { get; }

    /// <summary>Crea la pista para un ID; si ya está en caché trae su título.</summary>
    public TrackInfo CreateTrackInfo(string videoId)
    {
        if (!YouTubeUrl.IsValidVideoId(videoId))
            throw new ArgumentException($"ID de vídeo no válido: {videoId}", nameof(videoId));
        return TryReadCached(videoId, out var info) ? ToTrackInfo(info) : new TrackInfo(SourceName, videoId, $"YouTube {videoId}");
    }

    public bool IsCached(string videoId) => TryReadCached(videoId, out _);

    /// <summary>Pistas ya descargadas.</summary>
    public async IAsyncEnumerable<TrackInfo> BrowseAsync(
        string? location, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Directory.Exists(CacheFolder))
            yield break;

        string[] ids = await Task.Run(
            () => Directory.EnumerateFiles(CacheFolder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().ToArray(),
            ct).ConfigureAwait(false);

        foreach (string id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (TryReadCached(id, out var info))
                yield return ToTrackInfo(info);
        }
    }

    public async Task<TrackMedia> OpenAsync(TrackInfo track, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.SourceName != SourceName)
            throw new ArgumentException($"La pista no pertenece a la fuente '{SourceName}'.", nameof(track));
        if (!YouTubeUrl.IsValidVideoId(track.Id))
            throw new ArgumentException($"ID de vídeo no válido: {track.Id}", nameof(track));

        string id = track.Id;
        // Si las dos pletinas piden el mismo vídeo, la segunda espera y lo toma de la caché.
        var gate = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!TryReadCached(id, out var info))
                info = await DownloadAsync(id, progress, ct).ConfigureAwait(false);

            progress?.Report(1);
            return TrackMedia.FromFile(ToTrackInfo(info), AudioPath(id));
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<YouTubeDownloadInfo> DownloadAsync(string id, IProgress<double>? progress, CancellationToken ct)
    {
        string exe = _findExecutable()
            ?? throw new YouTubeSourceException("No se encuentra yt-dlp. Instálalo con: winget install yt-dlp.yt-dlp");

        Directory.CreateDirectory(CacheFolder);
        string outputTemplate = Path.Combine(CacheFolder, "%(id)s.%(ext)s");

        YouTubeDownloadInfo? info = null;
        string? lastError = null;
        void OnLine(string line)
        {
            if (YtDlpOutput.TryParseProgress(line) is { } fraction)
                progress?.Report(fraction);
            else if (YtDlpOutput.TryParseMeta(line) is { } meta)
                info = meta;
            else if (YtDlpOutput.TryParseError(line) is { } error)
                lastError = error;
        }

        YtDlp.RunResult result;
        try
        {
            result = await YtDlp.RunAsync(
                exe, YtDlpOutput.DownloadArguments(YouTubeUrl.ToWatchUrl(id), outputTemplate), OnLine, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DeletePartialFiles(id);
            throw;
        }

        if (result.ExitCode != 0 || info is null || !File.Exists(AudioPath(id)))
        {
            DeletePartialFiles(id);
            throw new YouTubeSourceException(lastError is not null
                ? $"No se pudo descargar: {lastError}"
                : $"No se pudo descargar el audio (yt-dlp terminó con código {result.ExitCode}).");
        }

        // El .json marca la descarga como completa: se escribe de forma atómica y al final.
        string tmp = MetaPath(id) + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(info, JsonOptions), CancellationToken.None)
            .ConfigureAwait(false);
        File.Move(tmp, MetaPath(id), overwrite: true);
        return info;
    }

    private bool TryReadCached(string id, out YouTubeDownloadInfo info)
    {
        info = null!;
        try
        {
            if (!File.Exists(MetaPath(id)) || !File.Exists(AudioPath(id)) || new FileInfo(AudioPath(id)).Length == 0)
                return false;
            var read = JsonSerializer.Deserialize<YouTubeDownloadInfo>(File.ReadAllText(MetaPath(id)), JsonOptions);
            if (read is null || read.Id != id)
                return false;
            info = read;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void DeletePartialFiles(string id)
    {
        if (!Directory.Exists(CacheFolder))
            return;
        foreach (string file in Directory.EnumerateFiles(CacheFolder, id + ".*"))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // yt-dlp puede tardar un instante en soltar el archivo; se reintentará en la próxima descarga.
            }
        }
    }

    private string AudioPath(string id) => Path.Combine(CacheFolder, id + ".m4a");

    private string MetaPath(string id) => Path.Combine(CacheFolder, id + ".json");

    private static TrackInfo ToTrackInfo(YouTubeDownloadInfo info) => new(
        SourceName,
        info.Id,
        info.Title,
        info.Artist,
        info.DurationSeconds is { } s ? TimeSpan.FromSeconds(s) : null);
}
