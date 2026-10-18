using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DJTube.Core.Abstractions;
using DJTube.Core.Localization;
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

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly Func<string?> _findExecutable;
    private readonly Func<string, CancellationToken, Task<byte[]>> _downloadBytes;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <param name="downloadBytes">Descarga de una URL (para las miniaturas); sustituible en los tests.</param>
    public YouTubeTrackSource(
        string? cacheFolder = null,
        Func<string?>? findExecutable = null,
        Func<string, CancellationToken, Task<byte[]>>? downloadBytes = null)
    {
        CacheFolder = cacheFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DJTube", "Cache", "YouTube");
        _findExecutable = findExecutable ?? YtDlp.Find;
        _downloadBytes = downloadBytes ?? ((url, ct) => Http.GetByteArrayAsync(url, ct));
    }

    /// <summary>
    /// Miniatura del vídeo (JPEG 320×180 de YouTube), guardada en la caché junto al audio.
    /// Si no se puede descargar devuelve null: no es imprescindible para pinchar.
    /// </summary>
    public async Task<string?> GetArtworkAsync(TrackInfo track, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.SourceName != SourceName || !YouTubeUrl.IsValidVideoId(track.Id))
            return null;

        string path = ArtworkPath(track.Id);
        if (File.Exists(path))
            return path;

        try
        {
            byte[] image = await _downloadBytes($"https://i.ytimg.com/vi/{track.Id}/mqdefault.jpg", ct).ConfigureAwait(false);
            if (image.Length == 0)
                return null;
            Directory.CreateDirectory(CacheFolder);
            string tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, image, CancellationToken.None).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }

    public string Name => SourceName;

    public string CacheFolder { get; }

    /// <summary>Crea la pista para un ID; si ya está en caché trae su título.</summary>
    public TrackInfo CreateTrackInfo(string videoId)
    {
        if (!YouTubeUrl.IsValidVideoId(videoId))
            throw new ArgumentException(Messages.InvalidVideoId(videoId), nameof(videoId));
        return TryReadCached(videoId, out var info) ? ToTrackInfo(info) : new TrackInfo(SourceName, videoId, $"YouTube {videoId}");
    }

    public bool IsCached(string videoId) => TryReadCached(videoId, out _);

    /// <summary>Se ha descargado una pista nueva (para refrescar la biblioteca). Llega desde un hilo de fondo.</summary>
    public event EventHandler<TrackInfo>? TrackAdded;

    /// <summary>Archivo de audio en la caché (exista o no).</summary>
    public string GetAudioPath(string videoId) => AudioPath(videoId);

    /// <summary>Cambia artista y título de una pista de la caché (edición desde la biblioteca).</summary>
    public TrackInfo? UpdateMetadata(string videoId, string? artist, string title)
    {
        if (!YouTubeUrl.IsValidVideoId(videoId) || !TryReadCached(videoId, out var info) || string.IsNullOrWhiteSpace(title))
            return null;
        var updated = info with
        {
            Artist = string.IsNullOrWhiteSpace(artist) ? null : artist.Trim(),
            Title = title.Trim(),
        };
        WriteMeta(updated);
        return ToTrackInfo(updated);
    }

    /// <summary>Quita una pista de la caché (audio, datos y miniatura).</summary>
    public void Delete(string videoId)
    {
        if (YouTubeUrl.IsValidVideoId(videoId))
            DeletePartialFiles(videoId);
    }

    /// <summary>Tamaño de la caché (audio, datos y miniaturas) en bytes.</summary>
    public long CacheSizeBytes()
    {
        if (!Directory.Exists(CacheFolder))
            return 0;
        return new DirectoryInfo(CacheFolder).EnumerateFiles().Sum(f => f.Length);
    }

    /// <summary>
    /// Libera espacio hasta quedar por debajo de <paramref name="maxBytes"/>: borra primero las canciones que
    /// hace más tiempo que no se cargan, sin tocar las de <paramref name="keep"/> (en listas, en un plato).
    /// Devuelve los IDs borrados.
    /// </summary>
    public IReadOnlyList<string> FreeSpace(long maxBytes, IReadOnlySet<string> keep)
    {
        var deleted = new List<string>();
        long size = CacheSizeBytes();
        if (size <= maxBytes || !Directory.Exists(CacheFolder))
            return deleted;

        var candidates = Directory.EnumerateFiles(CacheFolder, "*.json")
            .Select(path => (Id: Path.GetFileNameWithoutExtension(path), Used: File.GetLastAccessTimeUtc(path)))
            .Where(c => YouTubeUrl.IsValidVideoId(c.Id) && !keep.Contains(c.Id))
            .OrderBy(c => c.Used)
            .ToList();
        foreach (var (id, _) in candidates)
        {
            if (size <= maxBytes)
                break;
            long freed = Directory.EnumerateFiles(CacheFolder, id + ".*").Sum(f => new FileInfo(f).Length);
            DeletePartialFiles(id);
            size -= freed;
            deleted.Add(id);
        }
        return deleted;
    }

    /// <summary>Marca la canción como usada ahora (para el límite de caché).</summary>
    private void Touch(string id)
    {
        try
        {
            File.SetLastAccessTimeUtc(MetaPath(id), DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

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
            throw new ArgumentException(Messages.TrackSourceMismatch(SourceName), nameof(track));
        if (!YouTubeUrl.IsValidVideoId(track.Id))
            throw new ArgumentException(Messages.InvalidVideoId(track.Id), nameof(track));

        var downloaded = await EnsureDownloadedAsync(track.Id, progress, ct).ConfigureAwait(false);
        Touch(track.Id);
        return TrackMedia.FromFile(downloaded, AudioPath(track.Id));
    }

    /// <summary>
    /// Descarga un vídeo a la caché si aún no está (para la biblioteca, sin cargarlo en un plato).
    /// Si lo piden a la vez una pletina y la biblioteca, el segundo espera y lo toma de la caché.
    /// </summary>
    public async Task<TrackInfo> EnsureDownloadedAsync(string videoId, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!YouTubeUrl.IsValidVideoId(videoId))
            throw new ArgumentException(Messages.InvalidVideoId(videoId), nameof(videoId));
        var gate = _locks.GetOrAdd(videoId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!TryReadCached(videoId, out var info))
                info = await DownloadAsync(videoId, progress, ct).ConfigureAwait(false);
            progress?.Report(1);
            return ToTrackInfo(info);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Busca en YouTube (como el buscador de la web) y devuelve los primeros vídeos.</summary>
    public async Task<IReadOnlyList<YouTubePlaylistEntry>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        string exe = _findExecutable()
            ?? throw new YouTubeSourceException(Messages.YtDlpNotFound);

        var entries = new List<YouTubePlaylistEntry>();
        string? lastError = null;
        void OnLine(string line)
        {
            if (YtDlpOutput.TryParsePlaylistItem(line) is { } entry)
            {
                lock (entries)
                    entries.Add(entry);
            }
            else if (YtDlpOutput.TryParseError(line) is { } error)
                lastError = error;
        }

        var result = await YtDlp.RunAsync(exe, YtDlpOutput.SearchArguments(query), OnLine, ct).ConfigureAwait(false);
        if (entries.Count == 0 && (result.ExitCode != 0 || lastError is not null))
        {
            throw new YouTubeSourceException(lastError is not null
                ? Messages.DownloadFailed(lastError)
                : Messages.DownloadFailedExitCode(result.ExitCode));
        }
        return entries;
    }

    /// <summary>Lee el contenido de una lista de reproducción (sin descargar nada).</summary>
    public async Task<YouTubePlaylist> GetPlaylistAsync(string url, CancellationToken ct = default)
    {
        string exe = _findExecutable()
            ?? throw new YouTubeSourceException(Messages.YtDlpNotFound);

        var entries = new List<YouTubePlaylistEntry>();
        var seen = new HashSet<string>();
        string? title = null;
        string? lastError = null;
        void OnLine(string line)
        {
            if (YtDlpOutput.TryParsePlaylistItem(line) is { } entry)
            {
                lock (entries)
                {
                    if (seen.Add(entry.Id))
                        entries.Add(entry);
                }
            }
            else if (YtDlpOutput.TryParsePlaylistTitle(line) is { } t)
                title = t;
            else if (YtDlpOutput.TryParseError(line) is { } error)
                lastError = error;
        }

        int max = YouTubeUrl.TryGetPlaylistId(url, out string listId) && listId.StartsWith("RD", StringComparison.Ordinal)
            ? YtDlpOutput.MaxMixItems
            : YtDlpOutput.MaxPlaylistItems;
        var result = await YtDlp.RunAsync(exe, YtDlpOutput.PlaylistArguments(url, max), OnLine, ct).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            throw new YouTubeSourceException(lastError is not null
                ? Messages.DownloadFailed(lastError)
                : Messages.DownloadFailedExitCode(result.ExitCode));
        }
        return new YouTubePlaylist(title ?? "YouTube", entries);
    }

    private async Task<YouTubeDownloadInfo> DownloadAsync(string id, IProgress<double>? progress, CancellationToken ct)
    {
        string exe = _findExecutable()
            ?? throw new YouTubeSourceException(Messages.YtDlpNotFound);

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
                ? Messages.DownloadFailed(lastError)
                : Messages.DownloadFailedExitCode(result.ExitCode));
        }

        // El .json marca la descarga como completa: se escribe de forma atómica y al final.
        WriteMeta(info);
        TrackAdded?.Invoke(this, ToTrackInfo(info));
        return info;
    }

    private void WriteMeta(YouTubeDownloadInfo info)
    {
        string tmp = MetaPath(info.Id) + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(info, JsonOptions));
        File.Move(tmp, MetaPath(info.Id), overwrite: true);
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

            // Cachés anteriores a 0.8: guardaban el título del vídeo tal cual. Se separan artista y
            // título una sola vez (después, OriginalTitle ya existe y se respetan las ediciones).
            if (read.OriginalTitle is null)
            {
                var (artist, title) = TrackTitleParser.Parse(read.Title);
                read = read with { OriginalTitle = read.Title, Title = title, Artist = read.Artist ?? artist };
                try
                {
                    WriteMeta(read);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Se volverá a intentar la próxima vez.
                }
            }

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

    private string ArtworkPath(string id) => Path.Combine(CacheFolder, id + ".jpg");

    private static TrackInfo ToTrackInfo(YouTubeDownloadInfo info) => new(
        SourceName,
        info.Id,
        info.Title,
        info.Artist,
        info.DurationSeconds is { } s ? TimeSpan.FromSeconds(s) : null);
}
