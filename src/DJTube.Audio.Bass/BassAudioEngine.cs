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
/// Motor de audio sobre BASS. Grafo: platos → mezclador master (decode) → WASAPI.
/// </summary>
/// <remarks>Los métodos públicos se llaman desde el hilo de la interfaz.</remarks>
public sealed class BassAudioEngine : IAudioEngine
{
    private readonly BassDeck[] _decks;
    private int _mixer;
    private int _mixerRate;
    private int _mixerChannels;
    private WasapiOutput? _output;
    private bool _disposed;

    public BassAudioEngine(int deckCount = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(deckCount, 1);
        BassRuntime.EnsureInitialized();
        _decks = Enumerable.Range(0, deckCount).Select(_ => new BassDeck(GetOutputDelayBytes)).ToArray();
    }

    public IReadOnlyList<IDeck> Decks => _decks;

    public AudioOutputStatus? OutputStatus => _output?.Status;

    public double OutputDelaySeconds
    {
        get
        {
            var output = _output;
            return output is null || output.SampleRate <= 0 ? 0 : output.BufferedFrames / (double)output.SampleRate;
        }
    }

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

    public void StartOutput(AudioOutputSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);

        StopOutput();
        int device = settings.DeviceId ?? FindDefaultDevice();
        (int rate, int channels, _) = WasapiOutput.GetMixFormat(device);
        EnsureMixer(rate, channels);
        _output = WasapiOutput.Start(device, _mixer, rate, channels, settings.BufferMilliseconds);
    }

    public void StopOutput()
    {
        _output?.Dispose();
        _output = null;
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
        if (_mixer != 0)
            Bass.StreamFree(_mixer);
        _mixer = 0;
    }

    /// <summary>Crea (o recrea si cambia el formato) el mezclador master y le engancha los platos.</summary>
    private void EnsureMixer(int rate, int channels)
    {
        if (_mixer != 0 && _mixerRate == rate && _mixerChannels == channels)
            return;

        foreach (var deck in _decks)
            deck.Detach();
        if (_mixer != 0)
            Bass.StreamFree(_mixer);

        // NonStop: sigue entregando silencio sin fuentes activas (sin cortes en el dispositivo).
        // PositionEx: guarda historial de posiciones para compensar la latencia de salida.
        _mixer = BassMix.CreateMixerStream(
            rate, channels, BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop | BassFlags.MixerPositionEx);
        if (_mixer == 0)
            throw new BassException(Messages.MixerCreateFailed);
        _mixerRate = rate;
        _mixerChannels = channels;

        foreach (var deck in _decks)
            deck.AttachTo(_mixer);
    }

    private int GetOutputDelayBytes()
    {
        var output = _output;
        return output is null ? 0 : output.BufferedFrames * _mixerChannels * sizeof(float);
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
