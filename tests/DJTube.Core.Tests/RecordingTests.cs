using System.Buffers.Binary;
using System.Collections.Concurrent;
using DJTube.Core.Abstractions;
using DJTube.Core.Recording;

namespace DJTube.Core.Tests;

public sealed class RecordingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DJTubeRecTests_" + Guid.NewGuid().ToString("N"));

    public RecordingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Writer_ProducesValidWavHeader()
    {
        string path = Path.Combine(_dir, "a.wav");
        using (var writer = new WavFileWriter(path, 48000, 2))
            writer.Write(new float[48000 * 2]); // 1 s de silencio

        var header = Header(path);
        Assert.Equal(48000, header.Rate);
        Assert.Equal(2, header.Channels);
        Assert.Equal(16, header.Bits);
        Assert.Equal(48000 * 2 * 2, header.DataBytes);
        Assert.Equal(36 + header.DataBytes, header.RiffSize);
        Assert.Equal(WavFileWriter.HeaderBytes + header.DataBytes, new FileInfo(path).Length);
        Assert.Equal(TimeSpan.FromSeconds(1), WavFileWriter.ReadDuration(path));
    }

    [Fact]
    public void Writer_ConvertsAndClipsSamples()
    {
        string path = Path.Combine(_dir, "b.wav");
        using (var writer = new WavFileWriter(path, 44100, 1))
            writer.Write([0f, 1f, -1f, 2f, -2f, float.NaN, 0.5f]);

        short[] pcm = Pcm(path);
        Assert.InRange(pcm[0], (short)-1, (short)1); // silencio + dither de ±1
        Assert.InRange(pcm[1], (short)32766, short.MaxValue);
        Assert.InRange(pcm[2], (short)-32768, (short)-32766);
        Assert.Equal(short.MaxValue, pcm[3]);
        Assert.Equal(short.MinValue, pcm[4]);
        Assert.Equal(0, pcm[5]);
        Assert.InRange(pcm[6], (short)16382, (short)16385);
    }

    [Fact]
    public void Repair_RecoversAudioWrittenAfterLastCheckpoint()
    {
        string path = Path.Combine(_dir, "c.wav");
        var writer = new WavFileWriter(path, 48000, 2);
        writer.Write(new float[4800 * 2]);
        writer.Checkpoint();
        writer.Write(new float[4800 * 2 + 1]); // y medio fotograma suelto, como un corte en seco
        SimulateCrash(writer);

        Assert.Equal(4800 * 4, Header(path).DataBytes); // la cabecera solo sabe del punto de control
        Assert.True(WavFileWriter.Repair(path));

        var header = Header(path);
        Assert.Equal(9600 * 4, header.DataBytes);
        Assert.Equal(WavFileWriter.HeaderBytes + header.DataBytes, new FileInfo(path).Length);
        Assert.False(WavFileWriter.Repair(path)); // ya está bien
    }

    [Fact]
    public void Repair_IgnoresOtherFiles()
    {
        string path = Path.Combine(_dir, "otro.wav");
        File.WriteAllBytes(path, new byte[100]);
        Assert.False(WavFileWriter.Repair(path));
    }

    [Fact]
    public void Recorder_WritesWhatTheMasterPlays()
    {
        var tap = new FakeTap();
        using var recorder = NewRecorder(tap);
        string expected = recorder.Start(_dir);
        tap.Push(48000, 2, 0.25f, seconds: 1.5);

        recorder.Stop();

        Assert.Equal(RecorderState.Stopped, recorder.State);
        Assert.Equal(expected, recorder.CurrentFile);
        Assert.Equal(Path.Combine(_dir, "DJTube 2026-10-17 21.30.00.wav"), expected);
        Assert.Equal(TimeSpan.FromSeconds(1.5), WavFileWriter.ReadDuration(expected));
        Assert.Equal(TimeSpan.FromSeconds(1.5), recorder.Recorded);
        Assert.True(tap.Disposed);
    }

    [Fact]
    public void Recorder_PauseDropsWhatPlaysMeanwhile()
    {
        var tap = new FakeTap();
        using var recorder = NewRecorder(tap);
        recorder.Start(_dir);
        tap.Push(48000, 2, 0.1f, seconds: 1);
        recorder.Pause();
        Assert.Equal(RecorderState.Paused, recorder.State);
        tap.Push(48000, 2, 0.9f, seconds: 2);
        recorder.Resume();
        tap.Push(48000, 2, 0.1f, seconds: 0.5);
        recorder.Stop();

        Assert.Equal(TimeSpan.FromSeconds(1.5), WavFileWriter.ReadDuration(recorder.CurrentFile!));
        Assert.True(Pcm(recorder.CurrentFile!).All(s => Math.Abs(s) < 0.2 * 32768)); // nada de la pausa
    }

    [Fact]
    public void Recorder_SplitsIntoPartsWhenFileIsFull()
    {
        var tap = new FakeTap();
        // 1 s por archivo (48000 × 2 canales × 2 bytes)
        using var recorder = new SessionRecorder(() => tap, maxDataBytes: 192000, now: () => new DateTime(2026, 10, 17, 21, 30, 0));
        recorder.Start(_dir);
        for (int i = 0; i < 5; i++)
            tap.Push(48000, 2, 0.1f, seconds: 0.5);
        recorder.Stop();

        Assert.Equal(3, recorder.Files.Count);
        Assert.EndsWith("DJTube 2026-10-17 21.30.00 (3).wav", recorder.Files[2]);
        Assert.Equal(TimeSpan.FromSeconds(2.5), recorder.Recorded);
        Assert.Equal(2.5, recorder.Files.Sum(f => WavFileWriter.ReadDuration(f)!.Value.TotalSeconds), 6);
    }

    [Fact]
    public void Recorder_NewFileWhenOutputFormatChanges()
    {
        var tap = new FakeTap();
        using var recorder = NewRecorder(tap);
        recorder.Start(_dir);
        tap.Push(48000, 2, 0.1f, seconds: 1);
        recorder.Pause(); // asegura que el primer bloque está escrito con su formato
        recorder.Resume();
        tap.Push(44100, 2, 0.1f, seconds: 1);
        recorder.Stop();

        Assert.Equal(2, recorder.Files.Count);
        Assert.Equal(44100, Header(recorder.Files[1]).Rate);
        Assert.Equal(2, recorder.Recorded.TotalSeconds, 6);
    }

    [Fact]
    public void Recorder_CannotStartTwice()
    {
        var tap = new FakeTap();
        using var recorder = NewRecorder(tap);
        recorder.Start(_dir);
        Assert.Throws<InvalidOperationException>(() => recorder.Start(_dir));
    }

    [Fact]
    public void RecoverInterrupted_FixesOnlyDJTubeRecordings()
    {
        string ours = Path.Combine(_dir, "DJTube 2026-10-17 20.00.00.wav");
        var writer = new WavFileWriter(ours, 48000, 2);
        writer.Write(new float[9600]);
        SimulateCrash(writer); // sin ningún punto de control
        File.WriteAllBytes(Path.Combine(_dir, "otra cosa.wav"), new byte[60]);

        var recovered = SessionRecorder.RecoverInterrupted(_dir);

        Assert.Equal([ours], recovered);
        Assert.Equal(TimeSpan.FromSeconds(0.1), WavFileWriter.ReadDuration(ours));
    }

    private SessionRecorder NewRecorder(FakeTap tap) =>
        new(() => tap, now: () => new DateTime(2026, 10, 17, 21, 30, 0));

    /// <summary>Cierre en seco: lo escrito llega al sistema, pero sin actualizar la cabecera.</summary>
    private static void SimulateCrash(WavFileWriter writer)
    {
        var stream = (FileStream)typeof(WavFileWriter)
            .GetField("_stream", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(writer)!;
        stream.Flush();
        stream.SafeFileHandle.Dispose();
    }

    private static (int Rate, int Channels, int Bits, long DataBytes, long RiffSize) Header(string path)
    {
        byte[] h = File.ReadAllBytes(path).AsSpan(0, 44).ToArray();
        return (BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(24)), BinaryPrimitives.ReadInt16LittleEndian(h.AsSpan(22)),
            BinaryPrimitives.ReadInt16LittleEndian(h.AsSpan(34)), BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(40)),
            BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(4)));
    }

    private static short[] Pcm(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var samples = new short[(bytes.Length - 44) / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + i * 2));
        return samples;
    }

    /// <summary>Copia del master de prueba: devuelve lo que se le empuja, en bloques.</summary>
    private sealed class FakeTap : IAudioTap
    {
        private readonly ConcurrentQueue<(float[] Data, int Rate, int Channels)> _pending = new();
        private (float[] Data, int Rate, int Channels, int Offset)? _current;

        public bool Disposed { get; private set; }

        public void Push(int rate, int channels, float value, double seconds)
        {
            var data = new float[(int)Math.Round(rate * seconds) * channels];
            Array.Fill(data, value);
            _pending.Enqueue((data, rate, channels));
        }

        public TapRead Read(float[] buffer)
        {
            if (_current is null)
            {
                if (!_pending.TryDequeue(out var next))
                    return default;
                _current = (next.Data, next.Rate, next.Channels, 0);
            }
            var (data, rate, channels, offset) = _current.Value;
            int count = Math.Min(buffer.Length, data.Length - offset);
            Array.Copy(data, offset, buffer, 0, count);
            _current = offset + count >= data.Length ? null : (data, rate, channels, offset + count);
            return new TapRead(count, rate, channels);
        }

        public void Dispose() => Disposed = true;
    }
}
