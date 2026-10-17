using System.Security.Cryptography;
using System.Text;
using DJTube.Core.Abstractions;
using DJTube.Core.Models;

namespace DJTube.Core.Analysis;

/// <summary>
/// Guarda en disco el análisis de cada pista (onda y BPM) para no repetirlo: la segunda vez que se
/// carga una canción, la onda y el BPM aparecen al instante. La clave es la ruta, el tamaño y la
/// fecha del archivo: si cambia el archivo, se vuelve a analizar.
/// </summary>
public sealed class CachingTrackAnalyzer : ITrackAnalyzer
{
    private readonly ITrackAnalyzer _inner;

    public CachingTrackAnalyzer(ITrackAnalyzer inner, string cacheFolder)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        CacheFolder = cacheFolder;
    }

    public string CacheFolder { get; }

    public async Task<TrackAnalysis> AnalyzeAsync(string filePath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string? cachePath = CachePath(filePath);
        if (cachePath is not null && TryRead(cachePath, readWaveform: true) is { } cached)
        {
            progress?.Report(1);
            return cached;
        }

        var analysis = await _inner.AnalyzeAsync(filePath, progress, ct).ConfigureAwait(false);
        if (cachePath is not null)
            TryWrite(cachePath, analysis);
        return analysis;
    }

    public void SaveGrid(string filePath, BeatGrid? grid)
    {
        if (CachePath(filePath) is { } cachePath && TryRead(cachePath, readWaveform: true) is { } cached)
            TryWrite(cachePath, cached with { Grid = grid });
    }

    public BeatGrid? TryGetCachedGrid(string filePath) =>
        CachePath(filePath) is { } cachePath ? TryRead(cachePath, readWaveform: false)?.Grid : null;

    /// <summary>Borra el análisis guardado de un archivo (al quitarlo de la biblioteca).</summary>
    public void Forget(string filePath)
    {
        if (CachePath(filePath) is { } cachePath && File.Exists(cachePath))
            File.Delete(cachePath);
    }

    /// <summary>Formato binario: cabecera, rejilla y las cuatro bandas de la onda.</summary>
    public static void Write(Stream stream, TrackAnalysis analysis)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(FormatVersion);
        w.Write(analysis.Grid is not null);
        w.Write(analysis.Grid?.Bpm ?? 0);
        w.Write(analysis.Grid?.FirstBeatSeconds ?? 0);
        var wave = analysis.Waveform;
        w.Write(wave.BinsPerSecond);
        w.Write(wave.Length);
        foreach (float v in wave.Peak) w.Write(v);
        foreach (float v in wave.Low) w.Write(v);
        foreach (float v in wave.Mid) w.Write(v);
        foreach (float v in wave.High) w.Write(v);
    }

    /// <returns>null si no es un análisis válido de esta versión.</returns>
    public static TrackAnalysis? Read(Stream stream, bool readWaveform = true)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (r.ReadInt32() != Magic || r.ReadInt32() != FormatVersion)
            return null;
        bool hasGrid = r.ReadBoolean();
        double bpm = r.ReadDouble(), firstBeat = r.ReadDouble();
        BeatGrid? grid = hasGrid && bpm > 0 ? new BeatGrid(bpm, firstBeat) : null;

        double binsPerSecond = r.ReadDouble();
        int count = r.ReadInt32();
        if (binsPerSecond <= 0 || count < 0 || count > 50_000_000)
            return null;
        if (!readWaveform)
            return new TrackAnalysis(new WaveformData(binsPerSecond, [], [], [], []), grid);

        float[] ReadBand()
        {
            var band = new float[count];
            for (int i = 0; i < count; i++)
                band[i] = r.ReadSingle();
            return band;
        }
        float[] peak = ReadBand(), low = ReadBand(), mid = ReadBand(), high = ReadBand();
        return new TrackAnalysis(new WaveformData(binsPerSecond, peak, low, mid, high), grid);
    }

    private const int Magic = 0x41544A44; // "DJTA"
    private const int FormatVersion = 1;

    private string? CachePath(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
                return null;
            string key = $"{info.FullName.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            string hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(key)));
            return Path.Combine(CacheFolder, hash + ".djta");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static TrackAnalysis? TryRead(string cachePath, bool readWaveform)
    {
        try
        {
            if (!File.Exists(cachePath))
                return null;
            using var stream = new BufferedStream(File.OpenRead(cachePath));
            return Read(stream, readWaveform);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private void TryWrite(string cachePath, TrackAnalysis analysis)
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            string tmp = cachePath + ".tmp";
            using (var stream = new BufferedStream(File.Create(tmp)))
                Write(stream, analysis);
            File.Move(tmp, cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin caché funciona igual (más lento).
        }
    }
}
