using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace DJTube.App.Services;

/// <summary>Formato en el que se guarda una grabación (además del WAV original).</summary>
public enum RecordingFormat
{
    /// <summary>Solo el WAV (calidad CD, sin pérdida).</summary>
    Wav,

    /// <summary>MP3 a 320 kbps.</summary>
    Mp3,

    /// <summary>M4A (AAC) a 256 kbps.</summary>
    M4a,
}

/// <summary>
/// Convierte un WAV a MP3 o M4A con los codificadores que trae Windows (Media Foundation): no hace falta
/// instalar ni distribuir nada más.
/// </summary>
public static class AudioExporter
{
    private static readonly Lazy<bool> Started = new(() =>
    {
        MediaFoundationApi.Startup();
        return true;
    });

    public static string Extension(RecordingFormat format) => format switch
    {
        RecordingFormat.Mp3 => ".mp3",
        RecordingFormat.M4a => ".m4a",
        _ => ".wav",
    };

    /// <summary>Crea el archivo convertido junto al WAV y devuelve su ruta.</summary>
    public static Task<string> ExportAsync(string wavPath, RecordingFormat format, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (format == RecordingFormat.Wav)
            return Task.FromResult(wavPath);
        return Task.Run(() =>
        {
            _ = Started.Value;
            string output = Path.ChangeExtension(wavPath, Extension(format));
            // Media Foundation elige el contenedor por la extensión: el temporal también la lleva.
            string temp = Path.ChangeExtension(output, ".part" + Extension(format));
            try
            {
                using var reader = new WaveFileReader(wavPath);
                var source = new ProgressProvider(reader, progress, ct);
                if (format == RecordingFormat.Mp3)
                    MediaFoundationEncoder.EncodeToMp3(source, temp, 320_000);
                else
                    MediaFoundationEncoder.EncodeToAac(source, temp, 256_000);
            }
            catch
            {
                File.Delete(temp); // no dejar archivos a medias
                throw;
            }
            File.Move(temp, output, overwrite: true);
            progress?.Report(1);
            return output;
        }, ct);
    }

    /// <summary>Lee del WAV informando del avance (y permite cancelar).</summary>
    private sealed class ProgressProvider(WaveFileReader reader, IProgress<double>? progress, CancellationToken ct) : IWaveProvider
    {
        private long _read;
        private int _lastPercent = -1;

        public WaveFormat WaveFormat => reader.WaveFormat;

        public int Read(byte[] buffer, int offset, int count)
        {
            ct.ThrowIfCancellationRequested();
            int n = reader.Read(buffer, offset, count);
            _read += n;
            int percent = reader.Length > 0 ? (int)(100 * _read / reader.Length) : 0;
            if (percent != _lastPercent)
            {
                _lastPercent = percent;
                progress?.Report(percent / 100.0);
            }
            return n;
        }
    }
}
