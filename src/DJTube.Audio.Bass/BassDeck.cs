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
    private EqSettings _eq;
    private double _autoGainDb;
    private double _masterVolume = 1;
    private readonly float[] _levels = new float[2];

    /// <param name="outputDelayBytes">Bytes de salida del mezclador master que aún no se han oído.</param>
    public BassDeck(Func<int> outputDelayBytes)
    {
        _outputDelayBytes = outputDelayBytes;
    }

    /// <summary>Canales BASS de una pista cargada.</summary>
    private sealed record Chain(int Tempo, int Decoder, int MasterSplit, int CueSplit, double SourceRate, float CueBaseFrequency)
    {
        /// <summary>EQ de 3 bandas sobre el tempo stream (BQF de BASS_FX).</summary>
        public int LowFx { get; init; }
        public int MidFx { get; init; }
        public int HighFx { get; init; }

        /// <summary>Filtro de canal: solo existe mientras está activo (0 = apagado).</summary>
        public int FilterFx { get; set; }
        public FilterKind FilterKind { get; set; }

        /// <summary>Sync que hace el loop (0 = sin loop).</summary>
        public int LoopSync { get; set; }
    }

    /// <summary>
    /// Loop sin cortes: un sync "mixtime" en el final del loop salta al principio en el mismo momento en
    /// que el decodificador llega ahí (técnica de BASS para loops exactos). Es una única llamada, sin
    /// reservar memoria; el delegado es estático para que nunca se recoja.
    /// </summary>
    private static readonly SyncProcedure LoopJump = (_, channel, _, user) => Bass.ChannelSetPosition(channel, user.ToInt64());

    private LoopRegion? _loop;

    // Orden en la cadena: primero el EQ y después el filtro (mayor prioridad = antes).
    private const int EqPriority = 3;
    private const int FilterPriority = 2;

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

    public EqSettings Eq
    {
        get { lock (_sync) return _eq; }
        set
        {
            lock (_sync)
            {
                bool gainChanged = value.Gain != _eq.Gain;
                _eq = value;
                ApplyEqNoLock();
                if (gainChanged)
                    ApplyGainsNoLock();
            }
        }
    }

    public LoopRegion? Loop
    {
        get { lock (_sync) return _loop; }
    }

    public void SetLoop(LoopRegion loop)
    {
        lock (_sync)
        {
            if (_chain is not { } chain || loop.Length <= 0)
                return;
            RemoveLoopSyncNoLock(chain);
            long start = Bass.ChannelSeconds2Bytes(chain.Decoder, loop.Start);
            long end = Bass.ChannelSeconds2Bytes(chain.Decoder, loop.End);
            chain.LoopSync = Bass.ChannelSetSync(chain.Decoder, SyncFlags.Position | SyncFlags.Mixtime, end, LoopJump, new IntPtr(start));
            if (chain.LoopSync == 0)
                return;
            _loop = loop;

            // Si ya se ha pasado el final, se vuelve al principio (si no, el sync no saltaría nunca).
            double position = Bass.ChannelBytes2Seconds(chain.Decoder, Bass.ChannelGetPosition(chain.Tempo, PositionFlags.Bytes));
            if (position >= loop.End || position < loop.Start - 0.5)
                SeekNoLock(chain, loop.Start);
        }
    }

    public void ClearLoop()
    {
        lock (_sync)
        {
            if (_chain is { } chain)
                RemoveLoopSyncNoLock(chain);
            _loop = null;
        }
    }

    private static void RemoveLoopSyncNoLock(Chain chain)
    {
        if (chain.LoopSync != 0)
            Bass.ChannelRemoveSync(chain.Decoder, chain.LoopSync);
        chain.LoopSync = 0;
    }

    public double AutoGainDb
    {
        get { lock (_sync) return _autoGainDb; }
        set
        {
            lock (_sync)
            {
                _autoGainDb = double.IsFinite(value) ? Math.Clamp(value, -GainMath.MaxCutDb, GainMath.MaxBoostDb) : 0;
                ApplyGainsNoLock();
            }
        }
    }

    /// <summary>Volumen master (lo fija el motor para todos los platos).</summary>
    internal void SetMasterVolume(double volume)
    {
        lock (_sync)
        {
            _masterVolume = Math.Clamp(volume, 0, 1);
            ApplyGainsNoLock();
        }
    }

    /// <summary>Ganancia de entrada total: mando GAIN × ganancia automática.</summary>
    private double InputGainNoLock() => EqMath.GainFactor(_eq.Gain) * GainMath.DbToFactor(_autoGainDb);

    public (float Left, float Right) GetLevel()
    {
        lock (_sync)
        {
            if (_chain is not { } chain || _masterMixer == 0 || !_playing)
                return (0, 0);
            // Nivel de lo que está sonando (el mezclador guarda lo que ha leído: MixerChanBuffer).
            // No incluye el volumen del canal (fader), pero sí la ganancia de entrada, que va en ese volumen.
            if (BassMix.ChannelGetLevel(chain.MasterSplit, _levels, 0.03f, LevelRetrievalFlags.Stereo) <= 0)
                return (0, 0);
            float trim = (float)InputGainNoLock();
            return (_levels[0] * trim, _levels[1] * trim);
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
            _loop = null;
            _track = media.Track;
            _duration = Bass.ChannelBytes2Seconds(loaded.Decoder, Bass.ChannelGetLength(loaded.Decoder, PositionFlags.Bytes));
            _playing = false;
            ApplyPitchNoLock();
            ApplyEqNoLock();
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
            // Saltar fuera del loop lo quita (si no, al pasar por el final volvería atrás sin querer).
            if (_loop is { } loop && !loop.Contains(target))
            {
                RemoveLoopSyncNoLock(chain);
                _loop = null;
            }
            SeekNoLock(chain, target);
        }
    }

    private static void SeekNoLock(Chain chain, double seconds)
    {
        Bass.ChannelSetPosition(chain.Tempo, Bass.ChannelSeconds2Bytes(chain.Decoder, seconds), PositionFlags.Bytes);
        // Descarta lo que los splitters tenían ya leído de la posición anterior.
        BassMix.SplitStreamReset(chain.Tempo);
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
        return new Chain(tempo, decoder, masterSplit, cueSplit, sourceRate, cueFrequency)
        {
            // Si BASS_FX no pudiera crearlos, el plato suena igual (sin EQ): 0 = sin efecto.
            LowFx = Bass.ChannelSetFX(tempo, EffectType.BQF, EqPriority),
            MidFx = Bass.ChannelSetFX(tempo, EffectType.BQF, EqPriority),
            HighFx = Bass.ChannelSetFX(tempo, EffectType.BQF, EqPriority),
        };
    }

    // Graves y agudos con shelving, medios con campana; al mínimo, corte total (kill).
    private void ApplyEqNoLock()
    {
        if (_chain is not { } chain)
            return;
        SetBqf(chain.LowFx, BQFType.LowShelf, EqMath.LowShelfHz, EqMath.BandDb(_eq.Low));
        SetBqf(chain.MidFx, BQFType.PeakingEQ, EqMath.MidCenterHz, EqMath.BandDb(_eq.Mid), EqMath.MidBandwidthOctaves);
        SetBqf(chain.HighFx, BQFType.HighShelf, EqMath.HighShelfHz, EqMath.BandDb(_eq.High));
        ApplyFilterNoLock(chain);
    }

    private void ApplyFilterNoLock(Chain chain)
    {
        var filter = EqMath.Filter(_eq.Filter);
        if (filter.Kind == FilterKind.Off)
        {
            // Apagado: se quita de la cadena (en los extremos del barrido ya era casi transparente).
            if (chain.FilterFx != 0)
                Bass.ChannelRemoveFX(chain.Tempo, chain.FilterFx);
            chain.FilterFx = 0;
            chain.FilterKind = FilterKind.Off;
            return;
        }

        if (chain.FilterFx == 0)
            chain.FilterFx = Bass.ChannelSetFX(chain.Tempo, EffectType.BQF, FilterPriority);
        chain.FilterKind = filter.Kind;
        double cutoff = Math.Min(filter.CutoffHz, chain.SourceRate * 0.45);
        Bass.FXSetParameters(chain.FilterFx, new BQFParameters
        {
            lFilter = filter.Kind == FilterKind.LowPass ? BQFType.LowPass : BQFType.HighPass,
            fCenter = (float)cutoff,
            fBandwidth = 0,
            fQ = FilterResonance,
        });
    }

    /// <summary>Un poco de resonancia, como el filtro de un mezclador DJ.</summary>
    private const float FilterResonance = 1.1f;

    private static void SetBqf(int fx, BQFType type, double centerHz, double gainDb, double bandwidthOctaves = 0)
    {
        if (fx == 0)
            return;
        Bass.FXSetParameters(fx, new BQFParameters
        {
            lFilter = type,
            fCenter = (float)centerHz,
            fGain = (float)gainDb,
            fBandwidth = (float)bandwidthOctaves,
            fQ = 0,
            // La pendiente solo vale para los shelving: en la campana BASS_FX rechaza los parámetros.
            fS = type is BQFType.LowShelf or BQFType.HighShelf ? 1 : 0,
        });
    }

    private void AddToMixersNoLock(bool paused)
    {
        var chain = _chain!;
        var flags = paused ? BassFlags.MixerChanPause : BassFlags.Default;
        // MixerChanBuffer: el mezclador guarda lo que lee para poder medir el nivel (VU).
        if (_masterMixer != 0 && !BassMix.MixerAddChannel(_masterMixer, chain.MasterSplit, flags | BassFlags.MixerChanBuffer))
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
        // La ganancia de entrada (trim) afecta a las dos rutas; el fader solo al master.
        double trim = InputGainNoLock();
        Bass.ChannelSetAttribute(chain.MasterSplit, ChannelAttribute.Volume, _gain * trim * _masterVolume);
        Bass.ChannelSetAttribute(chain.CueSplit, ChannelAttribute.Volume,
            trim * HeadphoneMath.CueGain(_cueEnabled, _cueMix, _gain, _phonesVolume));
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
        _loop = null;
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
