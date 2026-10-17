using DJTube.Audio.Bass.Output;
using DJTube.Core.Abstractions;
using DJTube.Core.Localization;
using DJTube.Core.Models;
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
    private bool _disposed;

    public BassAudioEngine(int deckCount = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(deckCount, 1);
        BassRuntime.EnsureInitialized();
        _decks = Enumerable.Range(0, deckCount).Select(_ => new BassDeck(GetOutputDelayBytes)).ToArray();
    }

    /// <summary>Mezclador decode y su formato.</summary>
    private readonly record struct Mixer(int Handle, int Rate, int Channels)
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
            if (_wasapiMaster is { SampleRate: > 0 } wasapi)
                return wasapi.BufferedFrames / (double)wasapi.SampleRate;
            if (_asio is { SampleRate: > 0 } asio)
                return asio.LatencyFrames / (double)asio.SampleRate;
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
        FreeMixer(ref _master);
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
        _wasapiMaster = WasapiOutput.Start(masterDevice, _master.Handle, rate, channels, config.BufferMilliseconds);

        if (cueDevice is { } cue)
        {
            // Si falla la salida de auriculares, el master sigue sonando.
            try
            {
                _wasapiCue = WasapiOutput.Start(cue, _cue.Handle, cueRate, cueChannels, config.BufferMilliseconds);
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
            ConfigureMixers(asio.SampleRate, 2, withCue ? asio.SampleRate : 0, 2);
            asio.Start(_master.Handle, config.MasterChannel, withCue ? _cue.Handle : 0, config.CueChannel);
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
            FreeMixer(ref _master);
            // PositionEx: guarda historial de posiciones para compensar la latencia de salida.
            _master = CreateMixer(rate, channels, BassFlags.MixerPositionEx);
        }

        if (cueRate == 0)
            FreeMixer(ref _cue);
        else if (!_cue.Matches(cueRate, cueChannels))
        {
            FreeMixer(ref _cue);
            _cue = CreateMixer(cueRate, cueChannels, BassFlags.Default);
        }

        foreach (var deck in _decks)
            deck.AttachTo(_master.Handle, _cue.Handle);
        ApplyCueParameters();
    }

    private static Mixer CreateMixer(int rate, int channels, BassFlags extra)
    {
        // NonStop: sigue entregando silencio sin fuentes activas (sin cortes en el dispositivo).
        int handle = BassMix.CreateMixerStream(
            rate, channels, BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop | extra);
        if (handle == 0)
            throw new BassException(Messages.MixerCreateFailed);
        return new Mixer(handle, rate, channels);
    }

    private static void FreeMixer(ref Mixer mixer)
    {
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
        int frames = _wasapiMaster?.BufferedFrames ?? _asio?.LatencyFrames ?? 0;
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
