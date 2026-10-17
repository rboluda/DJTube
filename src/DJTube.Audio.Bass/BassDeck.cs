using DJTube.Core.Abstractions;
using DJTube.Core.Localization;
using DJTube.Core.Logic;
using DJTube.Core.Models;
using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;

namespace DJTube.Audio.Bass;

using Bass = ManagedBass.Bass;

/// <summary>
/// Un plato: archivo decodificado → BASS_FX tempo → dos splitters: uno al mezclador master y otro
/// al de auriculares (pre-escucha).
/// </summary>
/// <remarks>
/// Ninguna operación de esta clase se ejecuta en el hilo de audio. Play/pausa/seek/pitch/volumen solo
/// cambian flags, atributos o la posición de los canales (instantáneo). La carga se hace en segundo
/// plano y la pista nueva se engancha a los mezcladores cuando ya está lista.
/// Las posiciones están siempre en tiempo de la pista original (sin pitch).
///
/// El splitter de auriculares es "esclavo": solo lee lo que ya ha pedido el master, así que la salida
/// de auriculares nunca adelanta ni frena a la principal. Si cada salida tiene su propio reloj
/// (dos dispositivos), <see cref="MaintainCue"/> corrige la deriva remuestreando un poco la ruta de auriculares.
/// </remarks>
internal sealed class BassDeck : IDeck, IDisposable
{
    private readonly Lock _sync = new();
    private readonly Func<int> _outputDelayBytes;

    private int _masterMixer;
    private int _cueMixer;
    private Chain? _chain;
    private bool _playing;
    private int _loadGeneration;
    private TrackInfo? _track;
    private double _duration;
    private double _pitch;
    private bool _keyLock;
    private double _gain = 1;
    private bool _cueEnabled;
    private double _cueMix;
    private double _phonesVolume = 1;

    /// <param name="outputDelayBytes">Bytes de salida del mezclador master que aún no se han oído.</param>
    public BassDeck(Func<int> outputDelayBytes)
    {
        _outputDelayBytes = outputDelayBytes;
    }

    /// <summary>Canales BASS de una pista cargada.</summary>
    private sealed record Chain(int Tempo, int Decoder, int MasterSplit, int CueSplit, double SourceRate, float CueBaseFrequency);

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
        get { lock (_sync) return _chain is not null; }
    }

    public bool HasEnded
    {
        get { lock (_sync) return HasEndedNoLock(); }
    }

    public bool IsPlaying
    {
        get { lock (_sync) return _playing && _chain is not null && !HasEndedNoLock(); }
    }

    public double PitchPercent
    {
        get { lock (_sync) return _pitch; }
        set
        {
            lock (_sync)
            {
                _pitch = double.IsFinite(value) ? Math.Clamp(value, -50, 50) : 0;
                ApplyPitchNoLock();
            }
        }
    }

    public bool KeyLock
    {
        get { lock (_sync) return _keyLock; }
        set
        {
            lock (_sync)
            {
                _keyLock = value;
                ApplyPitchNoLock();
            }
        }
    }

    public double MasterGain
    {
        get { lock (_sync) return _gain; }
        set
        {
            lock (_sync)
            {
                _gain = double.IsFinite(value) ? Math.Max(0, value) : 0;
                ApplyGainsNoLock();
            }
        }
    }

    public bool CueEnabled
    {
        get { lock (_sync) return _cueEnabled; }
        set
        {
            lock (_sync)
            {
                _cueEnabled = value;
                ApplyGainsNoLock();
            }
        }
    }

    public double PositionSeconds
    {
        get
        {
            Chain? chain;
            bool fromMixer;
            lock (_sync)
            {
                chain = _chain;
                // En pausa el mezclador no lee el plato (su historial no ve los saltos) y no queda nada
                // por oír: la posición de la pista es exacta. Sonando, se compensa la latencia de salida.
                fromMixer = _masterMixer != 0 && _playing;
            }
            if (chain is null)
                return 0;

            long position = fromMixer
                ? BassMix.ChannelGetPosition(chain.MasterSplit, PositionFlags.Bytes, _outputDelayBytes())
                : -1;
            if (position < 0)
                position = Bass.ChannelGetPosition(chain.Tempo, PositionFlags.Bytes);
            return position < 0 ? 0 : Bass.ChannelBytes2Seconds(chain.Decoder, position);
        }
    }

    /// <summary>Mezcla y volumen de auriculares (los fija el motor para todos los platos).</summary>
    internal void SetCueParameters(double cueMix, double phonesVolume)
    {
        lock (_sync)
        {
            _cueMix = Math.Clamp(cueMix, 0, 1);
            _phonesVolume = Math.Clamp(phonesVolume, 0, 1);
            ApplyGainsNoLock();
        }
    }

    public async Task LoadAsync(TrackMedia media, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        int generation = Interlocked.Increment(ref _loadGeneration);

        Chain loaded = await Task.Run(() => CreateChain(media), ct).ConfigureAwait(false);

        Chain? old;
        lock (_sync)
        {
            if (generation != _loadGeneration || ct.IsCancellationRequested)
            {
                FreeInBackground(loaded);
                throw new OperationCanceledException(ct);
            }

            old = DetachChainNoLock();
            _chain = loaded;
            _track = media.Track;
            _duration = Bass.ChannelBytes2Seconds(loaded.Decoder, Bass.ChannelGetLength(loaded.Decoder, PositionFlags.Bytes));
            _playing = false;
            ApplyPitchNoLock();
            ApplyGainsNoLock();

            try
            {
                AddToMixersNoLock(paused: true);
            }
            catch
            {
                DetachChainNoLock();
                ResetTrackNoLock();
                FreeInBackground(loaded);
                FreeInBackground(old);
                throw;
            }
        }
        FreeInBackground(old);
    }

    public void Unload()
    {
        Interlocked.Increment(ref _loadGeneration);
        Chain? old;
        lock (_sync)
        {
            old = DetachChainNoLock();
            ResetTrackNoLock();
        }
        FreeInBackground(old);
    }

    public void Play()
    {
        lock (_sync)
        {
            if (_chain is null || HasEndedNoLock())
                return;
            SetPausedNoLock(false);
            _playing = true;
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_chain is null)
                return;
            SetPausedNoLock(true);
            _playing = false;
        }
    }

    public void Seek(double seconds)
    {
        lock (_sync)
        {
            if (_chain is not { } chain)
                return;

            // Al terminar la pista el canal deja de sonar pero sigue "sin pausa" en el mezclador:
            // se pausa para que volver atrás no lo arranque solo.
            if (_playing && HasEndedNoLock())
            {
                SetPausedNoLock(true);
                _playing = false;
            }

            double target = Math.Clamp(seconds, 0, Math.Max(0, _duration - 0.001));
            Bass.ChannelSetPosition(chain.Tempo, Bass.ChannelSeconds2Bytes(chain.Decoder, target), PositionFlags.Bytes);
            // Descarta lo que los splitters tenían ya leído de la posición anterior.
            BassMix.SplitStreamReset(chain.Tempo);
        }
    }

    /// <summary>Engancha el plato a los mezcladores (al arrancar o cambiar la salida). cueMixer 0 = sin auriculares.</summary>
    internal void AttachTo(int masterMixer, int cueMixer)
    {
        lock (_sync)
        {
            _masterMixer = masterMixer;
            _cueMixer = cueMixer;
            if (_chain is not null)
                AddToMixersNoLock(paused: !_playing);
        }
    }

    internal void Detach()
    {
        lock (_sync)
        {
            RemoveFromMixersNoLock();
            _masterMixer = 0;
            _cueMixer = 0;
        }
    }

    /// <summary>
    /// Mantiene la ruta de auriculares a ~30 ms detrás del master. Con dos relojes (dos dispositivos)
    /// ajusta un poco su velocidad para compensar la deriva; con uno (ASIO) solo vigila que no se acumule.
    /// </summary>
    internal void MaintainCue(bool cueActive, bool adaptive)
    {
        lock (_sync)
        {
            if (_chain is not { } chain)
                return;

            int available = BassMix.SplitStreamGetAvailable(chain.CueSplit);
            double seconds = available > 0 ? Bass.ChannelBytes2Seconds(chain.CueSplit, available) : 0;
            if (HeadphoneMath.ShouldReset(cueActive, seconds))
            {
                if (available > 0)
                    BassMix.SplitStreamReset(chain.CueSplit);
                return;
            }
            if (!adaptive || !_playing)
                return;

            double ratio = HeadphoneMath.DriftCorrection(seconds);
            Bass.ChannelSetAttribute(chain.CueSplit, ChannelAttribute.Frequency, chain.CueBaseFrequency * ratio);
        }
    }

    public void Dispose() => Unload();

    private static Chain CreateChain(TrackMedia media)
    {
        if (media.FilePath is not { } path)
            throw new NotSupportedException(Messages.OnlyFilesSupported);

        int decoder = Bass.CreateStream(
            path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan | BassFlags.AsyncFile);
        if (decoder == 0)
            throw new BassException(Messages.OpenFailed(Path.GetFileName(path)));

        int sourceRate = Bass.ChannelGetInfo(decoder).Frequency;
        int tempo = BassFx.TempoCreate(decoder, BassFlags.Decode | BassFlags.FxFreeSource);
        if (tempo == 0)
        {
            var error = Bass.LastError;
            Bass.StreamFree(decoder);
            throw new BassException(Messages.TempoFailed, error);
        }

        int masterSplit = BassMix.CreateSplitStream(tempo, BassFlags.Decode, null);
        int cueSplit = masterSplit == 0 ? 0 : BassMix.CreateSplitStream(tempo, BassFlags.Decode | BassFlags.SplitSlave, null);
        if (masterSplit == 0 || cueSplit == 0)
        {
            var error = Bass.LastError;
            if (masterSplit != 0)
                Bass.StreamFree(masterSplit);
            Bass.StreamFree(tempo);
            throw new BassException(Messages.SplitFailed, error);
        }

        float cueFrequency = Bass.ChannelGetInfo(cueSplit).Frequency;
        return new Chain(tempo, decoder, masterSplit, cueSplit, sourceRate, cueFrequency);
    }

    private void AddToMixersNoLock(bool paused)
    {
        var chain = _chain!;
        var flags = paused ? BassFlags.MixerChanPause : BassFlags.Default;
        if (_masterMixer != 0 && !BassMix.MixerAddChannel(_masterMixer, chain.MasterSplit, flags))
            throw new BassException(Messages.MixerAddFailed);
        if (_cueMixer != 0)
        {
            BassMix.SplitStreamReset(chain.CueSplit);
            if (!BassMix.MixerAddChannel(_cueMixer, chain.CueSplit, flags))
                throw new BassException(Messages.MixerAddFailed);
        }
    }

    private void RemoveFromMixersNoLock()
    {
        if (_chain is not { } chain)
            return;
        if (_masterMixer != 0)
            BassMix.MixerRemoveChannel(chain.MasterSplit);
        if (_cueMixer != 0)
            BassMix.MixerRemoveChannel(chain.CueSplit);
    }

    private void ApplyPitchNoLock()
    {
        if (_chain is not { } chain)
            return;
        var settings = PitchMath.ToTempoSettings(_pitch, _keyLock, chain.SourceRate);
        Bass.ChannelSetAttribute(chain.Tempo, ChannelAttribute.Tempo, settings.TempoPercent);
        Bass.ChannelSetAttribute(chain.Tempo, ChannelAttribute.TempoFrequency, settings.Frequency);
    }

    // Los mezcladores aplican el volumen de cada fuente (con rampa, sin chasquidos).
    // Auriculares: CUE (independiente de faders) mezclado con lo que suena en el master.
    private void ApplyGainsNoLock()
    {
        if (_chain is not { } chain)
            return;
        Bass.ChannelSetAttribute(chain.MasterSplit, ChannelAttribute.Volume, _gain);
        Bass.ChannelSetAttribute(chain.CueSplit, ChannelAttribute.Volume,
            HeadphoneMath.CueGain(_cueEnabled, _cueMix, _gain, _phonesVolume));
    }

    private bool HasEndedNoLock() =>
        _chain is { } chain && Bass.ChannelIsActive(chain.Tempo) == PlaybackState.Stopped;

    private void SetPausedNoLock(bool paused)
    {
        var chain = _chain!;
        var flags = paused ? BassFlags.MixerChanPause : BassFlags.Default;
        BassMix.ChannelFlags(chain.MasterSplit, flags, BassFlags.MixerChanPause);
        BassMix.ChannelFlags(chain.CueSplit, flags, BassFlags.MixerChanPause);
    }

    private void ResetTrackNoLock()
    {
        _chain = null;
        _track = null;
        _duration = 0;
        _playing = false;
    }

    /// <summary>Saca la pista actual de los mezcladores y la devuelve para liberarla fuera del lock.</summary>
    private Chain? DetachChainNoLock()
    {
        RemoveFromMixersNoLock();
        var old = _chain;
        _chain = null;
        return old;
    }

    /// <summary>Liberar streams puede esperar al hilo de lectura del archivo: no en el hilo de la UI.</summary>
    private static void FreeInBackground(Chain? chain)
    {
        if (chain is null)
            return;
        _ = Task.Run(() =>
        {
            Bass.StreamFree(chain.CueSplit);
            Bass.StreamFree(chain.MasterSplit);
            Bass.StreamFree(chain.Tempo); // libera también el decodificador (FxFreeSource)
        });
    }
}
