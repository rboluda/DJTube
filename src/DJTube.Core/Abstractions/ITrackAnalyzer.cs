using DJTube.Core.Models;

namespace DJTube.Core.Abstractions;

/// <summary>Calcula la forma de onda y el BPM de una pista (en segundo plano, fuera del hilo de audio).</summary>
public interface ITrackAnalyzer
{
    Task<TrackAnalysis> AnalyzeAsync(string filePath, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Guarda una rejilla corregida a mano (½, ×2) para la próxima vez. Por defecto no hace nada.</summary>
    void SaveGrid(string filePath, BeatGrid? grid)
    {
    }

    /// <summary>Rejilla guardada de un análisis anterior, sin analizar (para listas). Por defecto, ninguna.</summary>
    BeatGrid? TryGetCachedGrid(string filePath) => null;

    /// <summary>Lo guardado de una pista sin la onda (BPM, sonoridad, tonalidad), o null.</summary>
    TrackAnalysis? TryGetCachedSummary(string filePath) => null;

    /// <summary>¿Falta analizar (o completar el análisis de) este archivo?</summary>
    bool NeedsAnalysis(string filePath) => TryGetCachedGrid(filePath) is null;
}
