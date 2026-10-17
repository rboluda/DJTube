using DJTube.Core.Abstractions;

namespace DJTube.Core.Recording;

public enum RecorderState
{
    Stopped,
    Recording,
    Paused,
}

/// <summary>
/// Graba lo que suena por el master en WAV. Un hilo propio lee la copia del master cada pocos
/// milisegundos y escribe en disco; cada segundo actualiza la cabecera y vacía al disco, de modo que
/// un cierre inesperado pierde como mucho el último segundo (y <see cref="RecoverInterrupted"/> lo arregla).
/// En pausa lo que suena se descarta. Si el archivo se acerca al límite del WAV o cambia el formato
/// de la salida, la grabación sigue en un archivo nuevo («… (2).wav»).
/// </summary>
/// <remarks>Los métodos públicos se pueden llamar desde cualquier hilo.</remarks>
public sealed class SessionRecorder : IDisposable
{
    public const string FilePrefix = "DJTube ";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(1);

    private readonly Func<IAudioTap> _openTap;
    private readonly long _maxDataBytes;
    private readonly Func<DateTime> _now;
    private readonly Lock _sync = new();
    private readonly float[] _buffer = new float[48000 * 2];
    private readonly List<string> _files = [];

    private IAudioTap? _tap;
    private WavFileWriter? _writer;
    private Thread? _thread;
    private CancellationTokenSource? _stop;
    private string _baseName = "";
    private string _folder = "";
    private double _recordedSeconds; // lo grabado en los archivos anteriores (partes)
    private DateTime _lastCheckpoint;
    private volatile float _peak;

    /// <param name="openTap">Abre la copia del master (normalmente <see cref="IAudioEngine.OpenMasterTap"/>).</param>
    /// <param name="maxDataBytes">Tamaño de audio por archivo antes de pasar al siguiente.</param>
    public SessionRecorder(Func<IAudioTap> openTap, long maxDataBytes = WavFileWriter.DefaultMaxDataBytes, Func<DateTime>? now = null)
    {
        _openTap = openTap;
        _maxDataBytes = maxDataBytes;
        _now = now ?? (() => DateTime.Now);
    }

    public RecorderState State { get; private set; }

    /// <summary>Archivo en el que se está grabando (o el último).</summary>
    public string? CurrentFile { get { lock (_sync) return _writer?.Path ?? _files.LastOrDefault(); } }

    /// <summary>Todos los archivos de la grabación actual o la última (más de uno si se ha partido).</summary>
    public IReadOnlyList<string> Files { get { lock (_sync) return _files.ToList(); } }

    /// <summary>Tiempo grabado (sin las pausas).</summary>
    public TimeSpan Recorded
    {
        get
        {
            lock (_sync)
            {
                double current = _writer is { } w ? w.Frames / (double)w.SampleRate : 0;
                return TimeSpan.FromSeconds(_recordedSeconds + current);
            }
        }
    }


    /// <summary>Bytes escritos en total.</summary>
    public long Bytes { get { lock (_sync) return _bytesBefore + (_writer?.DataBytes + WavFileWriter.HeaderBytes ?? 0); } }

    private long _bytesBefore;

    /// <summary>Pico (0..1) desde la última consulta: para el medidor de la barra de grabación.</summary>
    public float TakePeak()
    {
        float peak = _peak;
        _peak = 0;
        return peak;
    }

    /// <summary>El último error de escritura (p. ej. disco lleno): la grabación se ha detenido.</summary>
    public string? Error { get; private set; }

    /// <summary>Cambia el estado (desde el hilo de grabación si hay un error).</summary>
    public event EventHandler? StateChanged;

    /// <summary>Empieza a grabar en <paramref name="folder"/>; devuelve la ruta del archivo.</summary>
    public string Start(string folder)
    {
        lock (_sync)
        {
            if (State != RecorderState.Stopped)
                throw new InvalidOperationException("Already recording.");
            Directory.CreateDirectory(folder);
            _folder = folder;
            _baseName = FilePrefix + _now().ToString("yyyy-MM-dd HH.mm.ss");
            _files.Clear();
            _bytesBefore = 0;
            _recordedSeconds = 0;
            Error = null;
            _tap = _openTap();
            _lastCheckpoint = DateTime.UtcNow;
            State = RecorderState.Recording;

            _stop = new CancellationTokenSource();
            var token = _stop.Token;
            _thread = new Thread(() => Run(token)) { IsBackground = true, Name = "DJTube recorder", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Path.Combine(folder, _baseName + ".wav");
    }

    /// <summary>Pausa: lo que ya ha sonado se guarda; lo que suene a partir de ahora, no.</summary>
    public void Pause()
    {
        lock (_sync)
        {
            if (State != RecorderState.Recording)
                return;
            Pump();
            State = RecorderState.Paused;
            CheckpointNoThrow();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (State != RecorderState.Paused)
                return;
            Pump(); // lo sonado durante la pausa se descarta
            State = RecorderState.Recording;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Guarda lo que queda y cierra el archivo. Se puede llamar aunque no se esté grabando.</summary>
    public void Stop()
    {
        Thread? thread;
        lock (_sync)
        {
            if (State == RecorderState.Stopped)
                return;
            _stop?.Cancel();
            thread = _thread;
        }
        thread?.Join(TimeSpan.FromSeconds(5));
        lock (_sync)
        {
            if (State == RecorderState.Recording)
                Pump();
            Close();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Arregla las grabaciones de DJTube de la carpeta que quedaron a medias por un cierre inesperado
    /// (la cabecera no cubre todo el audio). Devuelve las que ha recuperado.
    /// </summary>
    public static IReadOnlyList<string> RecoverInterrupted(string folder)
    {
        var recovered = new List<string>();
        if (!Directory.Exists(folder))
            return recovered;
        foreach (string file in Directory.EnumerateFiles(folder, FilePrefix + "*.wav"))
        {
            try
            {
                if (WavFileWriter.Repair(file))
                    recovered.Add(file);
            }
            catch (IOException)
            {
                // En uso (otra instancia grabando) o ilegible: se deja como está.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return recovered;
    }

    private void Run(CancellationToken token)
    {
        while (!token.WaitHandle.WaitOne(PollInterval))
        {
            bool failed;
            lock (_sync)
            {
                if (State == RecorderState.Stopped)
                    return;
                failed = !TryPump();
            }
            if (failed)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
        }
    }

    private bool TryPump()
    {
        try
        {
            Pump();
            if (DateTime.UtcNow - _lastCheckpoint >= CheckpointInterval)
            {
                _writer?.Checkpoint();
                _lastCheckpoint = DateTime.UtcNow;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
            Close();
            return false;
        }
    }

    /// <summary>Lee todo lo pendiente de la copia del master y lo escribe (o lo descarta en pausa).</summary>
    private void Pump()
    {
        if (_tap is null)
            return;
        while (true)
        {
            var read = _tap.Read(_buffer);
            if (read.Samples <= 0)
                return;
            if (State != RecorderState.Recording)
                continue;

            var samples = _buffer.AsSpan(0, read.Samples);
            float peak = _peak;
            foreach (float s in samples)
                peak = Math.Max(peak, Math.Abs(s));
            _peak = peak;

            var writer = WriterFor(read.SampleRate, read.Channels, samples.Length * 2);
            writer.Write(samples);
        }
    }

    /// <summary>El archivo actual, o uno nuevo si cambia el formato o no cabe más.</summary>
    private WavFileWriter WriterFor(int rate, int channels, long incomingBytes)
    {
        if (_writer is { } current
            && current.SampleRate == rate && current.Channels == channels
            && current.DataBytes + incomingBytes <= _maxDataBytes)
            return current;

        if (_writer is not null)
        {
            _recordedSeconds += _writer.Frames / (double)_writer.SampleRate;
            _bytesBefore += _writer.DataBytes + WavFileWriter.HeaderBytes;
            _writer.Dispose();
            _writer = null;
        }
        string name = _files.Count == 0 ? _baseName : $"{_baseName} ({_files.Count + 1})";
        string path = Path.Combine(_folder, name + ".wav");
        _writer = new WavFileWriter(path, rate, channels);
        _files.Add(path);
        return _writer;
    }

    private void Close()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error ??= ex.Message;
        }
        if (_writer is not null)
        {
            _recordedSeconds += _writer.Frames / (double)_writer.SampleRate;
            _bytesBefore += _writer.DataBytes + WavFileWriter.HeaderBytes;
        }
        _writer = null;
        _tap?.Dispose();
        _tap = null;
        State = RecorderState.Stopped;
    }

    private void CheckpointNoThrow()
    {
        try
        {
            _writer?.Checkpoint();
        }
        catch (IOException)
        {
        }
    }
}
