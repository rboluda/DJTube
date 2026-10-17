using DJTube.Audio.Bass.Native;
using DJTube.Core.Localization;
using DJTube.Core.Models;
using ManagedBass.Wasapi;

namespace DJTube.Audio.Bass.Output;

/// <summary>
/// Un dispositivo WASAPI (modo compartido) que reproduce un mezclador BASS de tipo decode.
/// BASSWASAPI tira del mezclador desde su propio hilo nativo.
/// </summary>
/// <remarks>
/// BASSWASAPI guarda el "dispositivo actual" por hilo, así que cada llamada fija antes el dispositivo.
/// </remarks>
internal sealed class WasapiOutput : IDisposable
{
    private readonly int _device;
    private readonly int _bytesPerDeviceFrame;
    private bool _disposed;

    private WasapiOutput(int device, string deviceName, WasapiInfo info)
    {
        _device = device;
        _bytesPerDeviceFrame = info.Channels * BytesPerSample(info.Format);
        SampleRate = info.Frequency;
        Channels = info.Channels;
        double latencyMs = info.BufferLength * 1000.0 / (info.Frequency * (double)_bytesPerDeviceFrame);
        Status = new AudioOutputStatus("WASAPI", deviceName, info.Frequency, info.Channels, latencyMs);
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public AudioOutputStatus Status { get; }

    /// <summary>Formato de mezcla del dispositivo (el que exige el modo compartido).</summary>
    public static (int SampleRate, int Channels, string Name) GetMixFormat(int device)
    {
        if (!BassWasapi.GetDeviceInfo(device, out WasapiDeviceInfo info))
            throw new BassException(Messages.InvalidOutputDevice(device));
        return (info.MixFrequency, info.MixChannels, info.Name);
    }

    /// <summary>Abre el dispositivo alimentado por <paramref name="mixer"/> y empieza a sonar.</summary>
    public static WasapiOutput Start(int device, int mixer, int sampleRate, int channels, double bufferMs)
    {
        float buffer = (float)(Math.Max(0, bufferMs) / 1000.0);
        bool ok = BassWasapiNative.InitFromChannel(
            device, sampleRate, channels, WasapiInitFlags.Shared | WasapiInitFlags.EventDriven, buffer, mixer);
        if (!ok)
        {
            // Algunos controladores no admiten el modo por eventos.
            ok = BassWasapiNative.InitFromChannel(
                device, sampleRate, channels, WasapiInitFlags.Shared, buffer, mixer);
        }
        if (!ok)
            throw new BassException(Messages.OutputOpenFailed);

        BassWasapi.CurrentDevice = device;
        WasapiInfo info = BassWasapi.Info;
        var output = new WasapiOutput(device, GetMixFormat(device).Name, info);

        if (!BassWasapi.Start())
        {
            var error = ManagedBass.Bass.LastError;
            BassWasapi.Free();
            throw new BassException(Messages.OutputStartFailed, error);
        }
        return output;
    }

    /// <summary>Frames ya mezclados que aún no se han oído (en el buffer del dispositivo).</summary>
    public int BufferedFrames
    {
        get
        {
            if (_disposed)
                return 0;
            BassWasapi.CurrentDevice = _device;
            int bytes = BassWasapi.GetData(IntPtr.Zero, (int)ManagedBass.DataFlags.Available);
            return bytes > 0 ? bytes / _bytesPerDeviceFrame : 0;
        }
    }

    private readonly float[] _levels = new float[8];

    /// <summary>Nivel de pico (0..1) de lo que suena por el dispositivo (los dos primeros canales).</summary>
    public (float Left, float Right) GetLevel()
    {
        if (_disposed)
            return (0, 0);
        BassWasapi.CurrentDevice = _device;
        if (BassWasapi.GetLevel(_levels, 0.03f, ManagedBass.LevelRetrievalFlags.Stereo) <= 0)
            return (0, 0);
        return (_levels[0], _levels[1]);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        BassWasapi.CurrentDevice = _device;
        BassWasapi.Stop(true);
        BassWasapi.Free();
    }

    private static int BytesPerSample(WasapiFormat format) => format switch
    {
        WasapiFormat.Bit8 => 1,
        WasapiFormat.Bit16 => 2,
        WasapiFormat.Bit24 => 3,
        _ => 4,
    };
}
