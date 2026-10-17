using DJTube.Audio.Bass.Output;
using DJTube.Core.Abstractions;
using DJTube.Core.Localization;
using DJTube.Core.Models;
using DJTube.Core.Logic;
using ManagedBass;

using ManagedBass.Mix;
using ManagedBass.Wasapi;

namespace DJTube.Audio.Bass;

using Bass = ManagedBass.Bass;

/// <summary>
/// Motor de audio sobre BASS. Grafo: cada plato → splitter → mezclador master (decode) → salida,
/// y otro splitter → mezclador de auriculares (decode) → salida de pre-escucha.
/// Salidas: WASAPI (uno o dos dispositivos) o ASIO (una tarjeta, dos pares de canales).
/// </summary>
/// <remarks>Los métodos públicos se llaman desde el hilo de la interfaz.</remarks>
public sealed class BassAudioEngine : IAudioEngine
{
    private readonly BassDeck[] _decks;
    private Mixer _master;
    private Mixer _cue;
    private WasapiOutput? _wasapiMaster;
    private WasapiOutput? _wasapiCue;
    private AsioOutput? _asio;
    private double _cueMix;
    private double _headphoneVolume = 1;
    private double _masterVolume = 1;
    private bool _limiter = true;
    private int _limiterDsp;
    private volatile PeakLimiter? _peakLimiter;
    private readonly DSPProcedure _limiterProc;
    private readonly BassMasterTap _meter;
    private readonly float[] _meterBuffer = new float[8192];
    private bool _disposed;
    private readonly List<BassMasterTap> _taps = [];

    public BassAudioEngine(int deckCount = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(deckCount, 1);
        BassRuntime.EnsureInitialized();
        _decks = Enumerable.Range(0, deckCount).Select(_ => new BassDeck(GetOutputDelayBytes)).ToArray();
        _limiterProc = LimiterDsp;
        // Copia del master para el VU (como la de la grabación): mide exactamente lo que sale.
        _meter = new BassMasterTap(_ => { });
        _taps.Add(_meter);
    }

    /// <summary>
    /// Mezclador decode y su formato. <paramref name="Output"/> es lo que lee el dispositivo: en el master,
    /// un splitter del mezclador (así la grabación puede sacar una copia exacta); en auriculares, el propio mezclador.
    /// </summary>
    private readonly record struct Mixer(int Handle, int Output, int Rate, int Channels)
    {
        public bool Matches(int rate, int channels) => Handle != 0 && Rate == rate && Channels == channels;
    }

    public IReadOnlyList<IDeck> Decks => _decks;

    public AudioOutputStatus? OutputStatus => _wasapiMaster?.Status ?? _asio?.MasterStatus;

    public AudioOutputStatus? CueOutputStatus => _wasapiCue?.Status ?? _asio?.CueStatus;

    public string? CueOutputError { get; private set; }

    public double OutputDelaySeconds
    {
        get
        {
            int limiter = _peakLimiter?.LatencyFrames ?? 0;
            if (_wasapiMaster is { SampleRate: > 0 } wasapi)
                return (wasapi.BufferedFrames + limiter) / (double)wasapi.SampleRate;
            if (_asio is { SampleRate: > 0 } asio)
                return (asio.LatencyFrames + limiter) / (double)asio.SampleRate;
            return 0;
        }
    }

    public double CueMix
    {
        get => _cueMix;
        set
        {
            _cueMix = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
            ApplyCueParameters();
        }
    }

    public double HeadphoneVolume
    {
        get => _headphoneVolume;
        set
        {
            _headphoneVolume = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
            ApplyCueParameters();
        }
    }

    private bool CueActive => _wasapiCue is not null || _asio?.CueStatus is not null;

    public IReadOnlyList<AudioDevice> GetOutputDevices()
    {
        var devices = new List<AudioDevice>();
        for (int i = 0; BassWasapi.GetDeviceInfo(i, out WasapiDeviceInfo info); i++)
        {
            if (!info.IsInput && !info.IsLoopback && info.IsEnabled)
                devices.Add(new AudioDevice(i, info.Name, info.IsDefault));
        }
        return devices;
    }

    public IReadOnlyList<AudioDevice> GetAsioDevices() => AsioOutput.GetDevices();

    public void StartOutput(OutputConfig config)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(config);

        StopOutput();
        switch (config)
        {
            case WasapiOutputConfig wasapi:
                StartWasapi(wasapi);
                break;
            case AsioOutputConfig asio:
                StartAsio(asio);
                break;
            default:
                throw new ArgumentException(config.GetType().Name, nameof(config));
        }
    }

    public void StopOutput()
    {
        _wasapiCue?.Dispose();
        _wasapiCue = null;
        _wasapiMaster?.Dispose();
        _wasapiMaster = null;
        _asio?.Dispose();
        _asio = null;
        CueOutputError = null;
    }

    public double MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
            foreach (var deck in _decks)
                deck.SetMasterVolume(_masterVolume);
        }
    }

    public bool LimiterEnabled
    {
        get => _limiter;
        set
        {
            _limiter = value;
            ApplyLimiter();
        }
    }

    /// <summary>Pico de lo que ha salido por el master desde la última consulta (se llama en cada fotograma).</summary>
    public (float Left, float Right) GetMasterLevel()
    {
        float left = 0, right = 0;
        while (true)
        {
            var read = _meter.Read(_meterBuffer);
            if (read.Samples <= 0)
                break;
            int channels = Math.Max(1, read.Channels);
            for (int i = 0; i + channels - 1 < read.Samples; i += channels)
            {
                left = Math.Max(left, Math.Abs(_meterBuffer[i]));
                right = Math.Max(right, Math.Abs(_meterBuffer[i + (channels > 1 ? 1 : 0)]));
            }
        }
        return (left, right);
    }

    /// <summary>
    /// Limitador de picos (con anticipación) en la salida del mezclador master: ni el dispositivo ni la
    /// grabación reciben nunca nada por encima de −0,3 dBFS. Corre como DSP de BASS: sin reservar memoria
    /// ni bloquear.
    /// </summary>
    private void ApplyLimiter()
    {
        if (_master.Handle == 0)
            return;
        if (!_limiter)
        {
            if (_limiterDsp != 0)
                Bass.ChannelRemoveDSP(_master.Handle, _limiterDsp);
            _limiterDsp = 0;
            _peakLimiter = null;
            return;
        }
        if (_limiterDsp == 0)
        {
            _peakLimiter = new PeakLimiter(_master.Rate, _master.Channels);
            _limiterDsp = Bass.ChannelSetDSP(_master.Handle, _limiterProc, IntPtr.Zero, 0);
        }
    }

    private unsafe void LimiterDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        if (_peakLimiter is { } limiter && buffer != IntPtr.Zero && length > 0)
            limiter.Process(new Span<float>((void*)buffer, length / sizeof(float)));
    }

    public IAudioTap OpenMasterTap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tap = new BassMasterTap(t => { lock (_taps) _taps.Remove(t); });
        lock (_taps)
            _taps.Add(tap);
        tap.Attach(_master.Handle, _master.Rate, _master.Channels);
        return tap;
    }

    public void Maintain()
    {
        bool active = CueActive;
        // Con dos dispositivos WASAPI hay dos relojes: hace falta corregir la deriva.
        bool adaptive = _wasapiCue is not null;
        foreach (var deck in _decks)
            deck.MaintainCue(active, adaptive);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        StopOutput();
        foreach (var deck in _decks)
        {
            deck.Detach();
            deck.Dispose();
        }
        FreeMaster();
        FreeMixer(ref _cue);
    }

    private void StartWasapi(WasapiOutputConfig config)
    {
        int masterDevice = config.MasterDeviceId ?? FindDefaultDevice();
        (int rate, int channels, _) = WasapiOutput.GetMixFormat(masterDevice);

        int? cueDevice = config.CueDeviceId is { } id && id != masterDevice ? id : null;
        (int cueRate, int cueChannels) = (0, 0);
        if (cueDevice is { } c)
            (cueRate, cueChannels, _) = WasapiOutput.GetMixFormat(c);

        ConfigureMixers(rate, channels, cueRate, cueChannels);
        _wasapiMaster = WasapiOutput.Start(masterDevice, _master.Output, rate, channels, config.BufferMilliseconds);

        if (cueDevice is { } cue)
        {
            // Si falla la salida de auriculares, el master sigue sonando.
            try
            {
                _wasapiCue = WasapiOutput.Start(cue, _cue.Output, cueRate, cueChannels, config.BufferMilliseconds);
            }
            catch (BassException ex)
            {
                CueOutputError = ex.Message;
            }
        }
    }

    private void StartAsio(AsioOutputConfig config)
    {
        var asio = AsioOutput.Open(config.DeviceId);
        try
        {
            bool withCue = config.CueChannel >= 0;
            // Si la tarjeta no tiene los canales de auriculares, el master suena igual y se avisa.
            if (withCue && config.CueChannel + 1 >= asio.Outputs)
            {
                CueOutputError = Messages.AsioChannelsMissing(config.CueChannel + 1, config.CueChannel + 2, asio.Outputs);
                withCue = false;
            }
            ConfigureMixers(asio.SampleRate, 2, withCue ? asio.SampleRate : 0, 2);
            asio.Start(_master.Output, config.MasterChannel, withCue ? _cue.Output : 0, config.CueChannel);
            _asio = asio;
        }
        catch
        {
            asio.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Crea (o recrea si cambia el formato) los mezcladores y engancha los platos.
    /// cueRate 0 = sin mezclador de auriculares.
    /// </summary>
    private void ConfigureMixers(int rate, int channels, int cueRate, int cueChannels)
    {
        foreach (var deck in _decks)
            deck.Detach();

        if (!_master.Matches(rate, channels))
        {
            FreeMaster();
            // PositionEx: guarda historial de posiciones para compensar la latencia de salida.
            _master = CreateMixer(rate, channels, BassFlags.MixerPositionEx, withOutputSplit: true);
            _limiterDsp = 0;
            _peakLimiter = null;
            ApplyLimiter();
            lock (_taps)
            {
                foreach (var tap in _taps)
                    tap.Attach(_master.Handle, rate, channels);
            }
        }

        if (cueRate == 0)
            FreeMixer(ref _cue);
        else if (!_cue.Matches(cueRate, cueChannels))
        {
            FreeMixer(ref _cue);
            _cue = CreateMixer(cueRate, cueChannels, BassFlags.Default, withOutputSplit: false);
        }

        foreach (var deck in _decks)
            deck.AttachTo(_master.Handle, _cue.Handle);
        ApplyCueParameters();
    }

    private static Mixer CreateMixer(int rate, int channels, BassFlags extra, bool withOutputSplit)
    {
        // NonStop: sigue entregando silencio sin fuentes activas (sin cortes en el dispositivo).
        int handle = BassMix.CreateMixerStream(
            rate, channels, BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop | extra);
        if (handle == 0)
            throw new BassException(Messages.MixerCreateFailed);
        if (!withOutputSplit)
            return new Mixer(handle, handle, rate, channels);

        // El dispositivo lee este splitter (que pide al mezclador justo lo que necesita: misma latencia
        // y misma posición); los splitters esclavos de la grabación reciben una copia de lo mismo.
        int output = BassMix.CreateSplitStream(handle, BassFlags.Decode, null);
        if (output == 0)
        {
            var error = Bass.LastError;
            Bass.StreamFree(handle);
            throw new BassException(Messages.MixerCreateFailed, error);
        }
        return new Mixer(handle, output, rate, channels);
    }

    /// <summary>Libera el mezclador master soltando antes las copias de grabación.</summary>
    private void FreeMaster()
    {
        lock (_taps)
        {
            foreach (var tap in _taps)
                tap.Detach();
        }
        FreeMixer(ref _master);
    }

    private static void FreeMixer(ref Mixer mixer)
    {
        if (mixer.Output != 0 && mixer.Output != mixer.Handle)
            Bass.StreamFree(mixer.Output);
        if (mixer.Handle != 0)
            Bass.StreamFree(mixer.Handle);
        mixer = default;
    }

    private void ApplyCueParameters()
    {
        foreach (var deck in _decks)
            deck.SetCueParameters(_cueMix, _headphoneVolume);
    }

    private int GetOutputDelayBytes()
    {
        int frames = (_wasapiMaster?.BufferedFrames ?? _asio?.LatencyFrames ?? 0) + (_peakLimiter?.LatencyFrames ?? 0);
        return frames * _master.Channels * sizeof(float);
    }

    private static int FindDefaultDevice()
    {
        for (int i = 0; BassWasapi.GetDeviceInfo(i, out WasapiDeviceInfo info); i++)
        {
            if (info.IsDefault && !info.IsInput && !info.IsLoopback && info.IsEnabled)
                return i;
        }
        throw new BassException(Messages.NoDefaultOutput, Errors.Device);
    }
}
