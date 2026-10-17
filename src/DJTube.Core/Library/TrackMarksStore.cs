using System.Text.Json;
using DJTube.Core.Logic;

namespace DJTube.Core.Library;

/// <summary>
/// Hot cues guardados por pista (clave: fuente + ID), para que al volver a cargar una canción estén donde
/// se dejaron. Un único JSON, escrito de forma atómica.
/// </summary>
public sealed class TrackMarksStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _path;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, double?[]> _cues;

    public TrackMarksStore(string path)
    {
        _path = path;
        _cues = Load(path);
    }

    public static string Key(string sourceName, string trackId) => $"{sourceName}:{trackId}";

    /// <summary>Hot cues de una pista (siempre <see cref="HotCueMath.Count"/> huecos; null = vacío).</summary>
    public double?[] GetHotCues(string key)
    {
        lock (_sync)
        {
            var result = new double?[HotCueMath.Count];
            if (_cues.TryGetValue(key, out var saved))
                Array.Copy(saved, result, Math.Min(saved.Length, result.Length));
            return result;
        }
    }

    public void SetHotCues(string key, IReadOnlyList<double?> cues)
    {
        lock (_sync)
        {
            if (cues.All(c => c is null))
                _cues.Remove(key);
            else
                _cues[key] = cues.Take(HotCueMath.Count).ToArray();
            Save();
        }
    }

    /// <summary>Una pista borrada de la biblioteca se lleva sus hot cues.</summary>
    public void Forget(string key)
    {
        lock (_sync)
        {
            if (_cues.Remove(key))
                Save();
        }
    }

    private static Dictionary<string, double?[]> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, double?[]>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return [];
    }

    private void Save()
    {
        try
        {
            string? folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_cues, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No poder guardar los hot cues no debe interrumpir la sesión.
        }
    }
}
