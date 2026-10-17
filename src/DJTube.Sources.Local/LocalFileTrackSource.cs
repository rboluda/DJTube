using System.Runtime.CompilerServices;
using DJTube.Core.Abstractions;
using DJTube.Core.Models;

namespace DJTube.Sources.Local;

/// <summary>
/// Pistas desde archivos del disco. El <see cref="TrackInfo.Id"/> es la ruta completa del archivo.
/// </summary>
public sealed class LocalFileTrackSource : ITrackSource
{
    public const string SourceName = "Archivos locales";

    public static IReadOnlyList<string> SupportedExtensions { get; } =
        [".mp3", ".flac", ".wav", ".aac", ".m4a"];

    public string Name => SourceName;

    public static bool IsSupported(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static TrackInfo CreateTrackInfo(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string fullPath = Path.GetFullPath(filePath);
        return new TrackInfo(SourceName, fullPath, Path.GetFileNameWithoutExtension(fullPath));
    }

    public async IAsyncEnumerable<TrackInfo> BrowseAsync(
        string? location,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string folder = string.IsNullOrWhiteSpace(location)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
            : location;

        // La enumeración del disco puede tardar: se hace fuera del hilo que llama.
        string[] files = await Task.Run(
            () => Directory.EnumerateFiles(folder)
                .Where(IsSupported)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            ct).ConfigureAwait(false);

        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested();
            yield return CreateTrackInfo(file);
        }
    }

    public Task<TrackMedia> OpenAsync(TrackInfo track, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.SourceName != SourceName)
            throw new ArgumentException($"La pista no pertenece a la fuente '{SourceName}'.", nameof(track));
        if (!IsSupported(track.Id))
            throw new NotSupportedException($"Formato no admitido: {Path.GetExtension(track.Id)}");
        if (!File.Exists(track.Id))
            throw new FileNotFoundException("No se encuentra el archivo.", track.Id);

        return Task.FromResult(TrackMedia.FromFile(track, track.Id));
    }
}
