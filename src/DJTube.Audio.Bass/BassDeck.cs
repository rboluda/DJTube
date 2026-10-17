using DJTube.Core.Abstractions;
using DJTube.Core.Models;
using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;

namespace DJTube.Audio.Bass;

using Bass = ManagedBass.Bass;

/// <summary>
/// Un plato: archivo decodificado → BASS_FX tempo → mezclador master.
/// </summary>
/// <remarks>
/// Ninguna operación de esta clase se ejecuta en el hilo de audio. Play/pausa/seek solo cambian
/// flags o la posición del canal en el mezclador (instantáneo). La carga se hace en segundo
/// plano y la pista nueva se engancha al mezclador cuando ya está lista.
/// </remarks>
internal sealed class BassDeck : IDeck, IDisposable
{
    private readonly Lock _sync = new();
    private readonly Func<int> _outputDelayBytes;

    private int _mixer;
    private int _stream;
    private bool _playing;
    private int _loadGeneration;
    private TrackInfo? _track;
    private double _duration;

    /// <param name="outputDelayBytes">Bytes de salida del mezclador que aún no se han oído.</param>
    public BassDeck(Func<int> outputDelayBytes)
    {
        _outputDelayBytes = outputDelayBytes;
    }

    public TrackInfo? Track
    {
        get { lock (_sync) return _track; }
    }

    public double DurationSeconds
    {
        get { lock (_sync) return _duration; }
    }

    public bool IsLoaded
    {
        get { lock (_sync) return _stream != 0; }
    }

    public bool HasEnded
    {
        get { lock (_sync) return HasEndedNoLock(); }
    }

    public bool IsPlaying
    {
        get { lock (_sync) return _playing && _stream != 0 && !HasEndedNoLock(); }
    }

    public double PositionSeconds
    {
        get
        {
            int stream;
            bool inMixer;
            lock (_sync)
            {
                stream = _stream;
                inMixer = _mixer != 0;
            }
            if (stream == 0)
                return 0;

            long position = inMixer
                ? BassMix.ChannelGetPosition(stream, PositionFlags.Bytes, _outputDelayBytes())
                : -1;
            if (position < 0)
                position = Bass.ChannelGetPosition(stream, PositionFlags.Bytes);
            return position < 0 ? 0 : Bass.ChannelBytes2Seconds(stream, position);
        }
    }

    public async Task LoadAsync(TrackMedia media, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        int generation = Interlocked.Increment(ref _loadGeneration);

        (int stream, double duration) = await Task.Run(() => CreateStream(media), ct).ConfigureAwait(false);

        int old;
        lock (_sync)
        {
            if (generation != _loadGeneration || ct.IsCancellationRequested)
            {
                FreeInBackground(stream);
                throw new OperationCanceledException(ct);
            }

            old = DetachStreamNoLock();
            _stream = stream;
            _track = media.Track;
            _duration = duration;
            _playing = false;

            if (_mixer != 0 && !BassMix.MixerAddChannel(_mixer, stream, BassFlags.MixerChanPause))
            {
                var error = Bass.LastError;
                _stream = 0;
                _track = null;
                _duration = 0;
                FreeInBackground(stream);
                FreeInBackground(old);
                throw new BassException("No se pudo añadir la pista al mezclador", error);
            }
        }
        FreeInBackground(old);
    }

    public void Unload()
    {
        Interlocked.Increment(ref _loadGeneration);
        int old;
        lock (_sync)
        {
            old = DetachStreamNoLock();
            _track = null;
            _duration = 0;
            _playing = false;
        }
        FreeInBackground(old);
    }

    public void Play()
    {
        lock (_sync)
        {
            if (_stream == 0 || HasEndedNoLock())
                return;
            SetPausedNoLock(false);
            _playing = true;
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_stream == 0)
                return;
            SetPausedNoLock(true);
            _playing = false;
        }
    }

    public void Seek(double seconds)
    {
        lock (_sync)
        {
            if (_stream == 0)
                return;

            // Al terminar la pista el canal deja de sonar pero sigue "sin pausa" en el mezclador:
            // se pausa para que volver atrás no lo arranque solo.
            if (_playing && HasEndedNoLock())
            {
                SetPausedNoLock(true);
                _playing = false;
            }

            double target = Math.Clamp(seconds, 0, Math.Max(0, _duration - 0.001));
            long bytes = Bass.ChannelSeconds2Bytes(_stream, target);
            if (_mixer != 0)
                BassMix.ChannelSetPosition(_stream, bytes, PositionFlags.Bytes);
            else
                Bass.ChannelSetPosition(_stream, bytes, PositionFlags.Bytes);
        }
    }

    /// <summary>Engancha el plato a un mezclador (al arrancar o cambiar la salida).</summary>
    internal void AttachTo(int mixer)
    {
        lock (_sync)
        {
            _mixer = mixer;
            if (_stream != 0)
            {
                var flags = _playing ? BassFlags.Default : BassFlags.MixerChanPause;
                if (!BassMix.MixerAddChannel(mixer, _stream, flags))
                    throw new BassException("No se pudo añadir la pista al mezclador");
            }
        }
    }

    internal void Detach()
    {
        lock (_sync)
        {
            if (_stream != 0 && _mixer != 0)
                BassMix.MixerRemoveChannel(_stream);
            _mixer = 0;
        }
    }

    public void Dispose() => Unload();

    private static (int Stream, double Duration) CreateStream(TrackMedia media)
    {
        if (media.FilePath is not { } path)
            throw new NotSupportedException("De momento solo se pueden cargar archivos.");

        int decoder = Bass.CreateStream(
            path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan | BassFlags.AsyncFile);
        if (decoder == 0)
            throw new BassException($"No se pudo abrir «{Path.GetFileName(path)}»");

        double duration = Bass.ChannelBytes2Seconds(decoder, Bass.ChannelGetLength(decoder, PositionFlags.Bytes));

        int tempo = BassFx.TempoCreate(decoder, BassFlags.Decode | BassFlags.FxFreeSource);
        if (tempo == 0)
        {
            var error = Bass.LastError;
            Bass.StreamFree(decoder);
            throw new BassException("No se pudo crear el canal de tempo", error);
        }
        return (tempo, duration);
    }

    private bool HasEndedNoLock() =>
        _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Stopped;

    private void SetPausedNoLock(bool paused) =>
        BassMix.ChannelFlags(_stream, paused ? BassFlags.MixerChanPause : BassFlags.Default, BassFlags.MixerChanPause);

    /// <summary>Saca el stream actual del mezclador y lo devuelve para liberarlo fuera del lock.</summary>
    private int DetachStreamNoLock()
    {
        int old = _stream;
        if (old != 0 && _mixer != 0)
            BassMix.MixerRemoveChannel(old);
        _stream = 0;
        return old;
    }

    /// <summary>Liberar un stream puede esperar al hilo de lectura del archivo: no en el hilo de la UI.</summary>
    private static void FreeInBackground(int stream)
    {
        if (stream != 0)
            _ = Task.Run(() => Bass.StreamFree(stream));
    }
}
