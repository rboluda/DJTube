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
        var (cached, version) = cachePath is not null ? TryReadVersioned(cachePath, readWaveform: true) : (null, 0);
        if (cached is not null && version >= FormatVersion)
        {
            progress?.Report(1);
            return cached;
        }

        var analysis = await _inner.AnalyzeAsync(filePath, progress, ct).ConfigureAwait(false);
        // Un análisis de una versión anterior se rehace, pero se respeta su BPM si es el mismo tempo salvo
        // octavas: puede haberse corregido a mano con ½ / ×2. Si no tenía BPM, o era otro tempo (4/3 o 3/2
        // del real, fallos del detector anterior), vale el nuevo.
        if (cached?.Grid is { } previous && (analysis.Grid is null || SameTempoUpToOctaves(previous.Bpm, analysis.Grid.Bpm)))
            analysis = analysis with { Grid = previous };
        if (cachePath is not null)
            TryWrite(cachePath, analysis);
        return analysis;
    }

    /// <summary>Lo guardado de una pista sin la onda (BPM, sonoridad, tonalidad), o null si no se ha analizado.</summary>
    public TrackAnalysis? TryGetCachedSummary(string filePath) =>
        CachePath(filePath) is { } cachePath ? TryReadVersioned(cachePath, readWaveform: false).Analysis : null;

    /// <summary>¿Falta analizar (o completar el análisis de) este archivo?</summary>
    public bool NeedsAnalysis(string filePath) =>
        CachePath(filePath) is not { } cachePath || TryReadVersioned(cachePath, readWaveform: false).Version < FormatVersion;

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

    /// <summary>
    /// Formato binario (v2 y v3): cabecera, rejilla, sonoridad, tonalidad y las cuatro bandas de la onda.
    /// La v3 es igual que la v2; solo marca que el BPM viene del detector por barrido.
    /// </summary>
    public static void Write(Stream stream, TrackAnalysis analysis)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(FormatVersion);
        w.Write(analysis.Grid is not null);
        w.Write(analysis.Grid?.Bpm ?? 0);
        w.Write(analysis.Grid?.FirstBeatSeconds ?? 0);
        w.Write(analysis.LoudnessLufs is not null);
        w.Write(analysis.LoudnessLufs ?? 0);
        w.Write(analysis.Key is not null);
        w.Write(analysis.Key?.Code ?? 0);
        var wave = analysis.Waveform;
        w.Write(wave.BinsPerSecond);
        w.Write(wave.Length);
        foreach (float v in wave.Peak) w.Write(v);
        foreach (float v in wave.Low) w.Write(v);
        foreach (float v in wave.Mid) w.Write(v);
        foreach (float v in wave.High) w.Write(v);
    }

    /// <returns>null si no es un análisis válido.</returns>
    public static TrackAnalysis? Read(Stream stream, bool readWaveform = true) => ReadVersioned(stream, readWaveform).Analysis;

    /// <summary>Lee también la versión: la 1 no tenía sonoridad ni tonalidad; la 2, el detector de BPM anterior.</summary>
    private static (TrackAnalysis? Analysis, int Version) ReadVersioned(Stream stream, bool readWaveform)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int version;
        if (r.ReadInt32() != Magic || (version = r.ReadInt32()) is < 1 or > FormatVersion)
            return (null, 0);
        bool hasGrid = r.ReadBoolean();
        double bpm = r.ReadDouble(), firstBeat = r.ReadDouble();
        BeatGrid? grid = hasGrid && bpm > 0 ? new BeatGrid(bpm, firstBeat) : null;
        double? lufs = null;
        MusicalKey? key = null;
        if (version >= 2)
        {
            bool hasLufs = r.ReadBoolean();
            double l = r.ReadDouble();
            bool hasKey = r.ReadBoolean();
            int code = r.ReadInt32();
            lufs = hasLufs ? l : null;
            key = hasKey && code is >= 0 and < 24 ? MusicalKey.FromCode(code) : null;
        }

        double binsPerSecond = r.ReadDouble();
        int count = r.ReadInt32();
        if (binsPerSecond <= 0 || count < 0 || count > 50_000_000)
            return (null, 0);
        if (!readWaveform)
            return (new TrackAnalysis(new WaveformData(binsPerSecond, [], [], [], []), grid, lufs, key), version);

        float[] ReadBand()
        {
            var band = new float[count];
            for (int i = 0; i < count; i++)
                band[i] = r.ReadSingle();
            return band;
        }
        float[] peak = ReadBand(), low = ReadBand(), mid = ReadBand(), high = ReadBand();
        return (new TrackAnalysis(new WaveformData(binsPerSecond, peak, low, mid, high), grid, lufs, key), version);
    }

    private const int Magic = 0x41544A44; // "DJTA"
    private const int FormatVersion = 3;

    /// <summary>¿Son el mismo tempo salvo ½ / ×2 (con un margen del 1 %)?</summary>
    private static bool SameTempoUpToOctaves(double a, double b)
    {
        double octaves = Math.Log2(a / b);
        return Math.Abs(octaves - Math.Round(octaves)) < 0.015;
    }

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

    private static TrackAnalysis? TryRead(string cachePath, bool readWaveform) => TryReadVersioned(cachePath, readWaveform).Analysis;

    private static (TrackAnalysis? Analysis, int Version) TryReadVersioned(string cachePath, bool readWaveform)
    {
        try
        {
            if (!File.Exists(cachePath))
                return (null, 0);
            using var stream = new BufferedStream(File.OpenRead(cachePath));
            return ReadVersioned(stream, readWaveform);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, 0);
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
