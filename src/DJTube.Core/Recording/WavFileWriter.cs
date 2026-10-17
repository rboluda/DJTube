using System.Buffers.Binary;

namespace DJTube.Core.Recording;

/// <summary>
/// Escribe un WAV PCM de 16 bits a partir de muestras float. La cabecera se puede actualizar en
/// cualquier momento (<see cref="Checkpoint"/>): si el programa se cierra de golpe, el archivo es
/// válido hasta el último punto de control, y <see cref="Repair"/> recupera además lo escrito después.
/// </summary>
public sealed class WavFileWriter : IDisposable
{
    public const int HeaderBytes = 44;
    private const short BitsPerSample = 16;

    /// <summary>Límite del formato WAV (tamaños de 32 bits) con margen.</summary>
    public const long DefaultMaxDataBytes = 4_000_000_000;

    private readonly FileStream _stream;
    private readonly int _blockAlign;
    private byte[] _bytes = new byte[8192];
    private uint _noise = 0x9E3779B9;

    public WavFileWriter(string path, int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        Path = path;
        SampleRate = sampleRate;
        Channels = channels;
        _blockAlign = channels * BitsPerSample / 8;
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 1 << 16);
        _stream.Write(BuildHeader(sampleRate, channels, 0));
    }

    public string Path { get; }

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>Bytes de audio escritos (sin la cabecera).</summary>
    public long DataBytes { get; private set; }

    public long Frames => DataBytes / _blockAlign;

    /// <summary>Convierte a 16 bits (con dither triangular para no añadir distorsión) y escribe.</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        int needed = samples.Length * 2;
        if (_bytes.Length < needed)
            _bytes = new byte[needed];
        for (int i = 0; i < samples.Length; i++)
        {
            float s = samples[i];
            double value = float.IsFinite(s) ? s * 32767.0 + Dither() : 0;
            short pcm = (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(_bytes.AsSpan(i * 2), pcm);
        }
        _stream.Write(_bytes, 0, needed);
        DataBytes += needed;
    }

    /// <summary>Actualiza la cabecera con lo escrito y lo manda al disco (no solo a la caché de Windows).</summary>
    public void Checkpoint()
    {
        WriteSizes(_stream, DataBytes);
        _stream.Flush(flushToDisk: true);
    }

    public void Dispose()
    {
        try
        {
            Checkpoint();
        }
        finally
        {
            _stream.Dispose();
        }
    }

    /// <summary>
    /// Arregla la cabecera de un WAV escrito por esta clase que quedó a medias (cierre inesperado):
    /// los tamaños pasan a cubrir todo el audio que hay en el archivo. Devuelve true si lo ha cambiado.
    /// </summary>
    public static bool Repair(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (stream.Length < HeaderBytes)
            return false;
        Span<byte> header = stackalloc byte[HeaderBytes];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8)
            || !header[12..16].SequenceEqual("fmt "u8) || !header[36..40].SequenceEqual("data"u8))
            return false;

        int blockAlign = BinaryPrimitives.ReadInt16LittleEndian(header[32..]);
        if (blockAlign <= 0)
            return false;
        long data = (stream.Length - HeaderBytes) / blockAlign * blockAlign;
        data = Math.Min(data, uint.MaxValue - 36);
        uint written = BinaryPrimitives.ReadUInt32LittleEndian(header[40..]);
        if (written == data && stream.Length == HeaderBytes + data)
            return false;

        WriteSizes(stream, data);
        stream.SetLength(HeaderBytes + data); // fuera la muestra a medias del final
        stream.Flush(flushToDisk: true);
        return true;
    }

    /// <summary>Duración de un WAV según su cabecera (null si no es un WAV de esta clase).</summary>
    public static TimeSpan? ReadDuration(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[HeaderBytes];
        if (stream.Length < HeaderBytes)
            return null;
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("RIFF"u8))
            return null;
        int byteRate = BinaryPrimitives.ReadInt32LittleEndian(header[28..]);
        uint data = BinaryPrimitives.ReadUInt32LittleEndian(header[40..]);
        return byteRate > 0 ? TimeSpan.FromSeconds(data / (double)byteRate) : null;
    }

    private static byte[] BuildHeader(int sampleRate, int channels, uint dataBytes)
    {
        var h = new byte[HeaderBytes];
        "RIFF"u8.CopyTo(h);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4), 36 + dataBytes);
        "WAVE"u8.CopyTo(h.AsSpan(8));
        "fmt "u8.CopyTo(h.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(20), 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(24), sampleRate);
        int blockAlign = channels * BitsPerSample / 8;
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(28), sampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(32), (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(34), BitsPerSample);
        "data"u8.CopyTo(h.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(40), dataBytes);
        return h;
    }

    private static void WriteSizes(FileStream stream, long dataBytes)
    {
        long end = stream.Position;
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)(36 + dataBytes));
        stream.Position = 4;
        stream.Write(size);
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)dataBytes);
        stream.Position = 40;
        stream.Write(size);
        stream.Position = Math.Max(end, HeaderBytes + dataBytes);
    }

    /// <summary>Ruido triangular de ±1 LSB (diferencia de dos uniformes).</summary>
    private double Dither() => NextUniform() - NextUniform();

    private double NextUniform()
    {
        _noise ^= _noise << 13;
        _noise ^= _noise >> 17;
        _noise ^= _noise << 5;
        return _noise / (double)uint.MaxValue;
    }
}
