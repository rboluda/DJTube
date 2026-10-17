using DJTube.Core.Models;

namespace DJTube.Core.Abstractions;

/// <summary>Calcula la forma de onda y el BPM de una pista (en segundo plano, fuera del hilo de audio).</summary>
public interface ITrackAnalyzer
{
    Task<TrackAnalysis> AnalyzeAsync(string filePath, IProgress<double>? progress = null, CancellationToken ct = default);
}
