using DJTube.Core.Localization;
using DJTube.Core.Models;
using ManagedBass.Asio;

namespace DJTube.Audio.Bass.Output;

/// <summary>
/// Una tarjeta ASIO multicanal: el mezclador master en un par de canales y el de auriculares en otro.
/// BASSASIO lee los mezcladores directamente desde su hilo (sin código .NET en el hilo de audio).
/// </summary>
/// <remarks>Se abre en dos pasos: <see cref="Open"/> da la frecuencia (para crear los mezcladores) y
/// <see cref="Start"/> los conecta y arranca. BASSASIO guarda el dispositivo actual por hilo.</remarks>
internal sealed class AsioOutput : IDisposable
{
    private readonly int _device;
    private readonly string _name;
    private bool _started;
    private bool _disposed;

    private AsioOutput(int device, string name, int sampleRate, int outputs)
    {
        _device = device;
        _name = name;
        SampleRate = sampleRate;
        Outputs = outputs;
    }

    public int SampleRate { get; }

    public int Outputs { get; }

    public AudioOutputStatus? MasterStatus { get; private set; }

    public AudioOutputStatus? CueStatus { get; private set; }

    /// <summary>Latencia de salida en frames (lo que tarda en oírse lo que se mezcla).</summary>
    public int LatencyFrames { get; private set; }

    public static IReadOnlyList<AudioDevice> GetDevices()
    {
        var devices = new List<AudioDevice>();
        for (int i = 0; BassAsio.GetDeviceInfo(i, out AsioDeviceInfo info); i++)
            devices.Add(new AudioDevice(i, info.Name, false));
        return devices;
    }

    public static AsioOutput Open(int device)
    {
        string name = BassAsio.GetDeviceInfo(device, out AsioDeviceInfo deviceInfo) ? deviceInfo.Name : $"#{device}";
        if (!BassAsio.Init(device, AsioInitFlags.Thread))
            throw new BassException(Messages.AsioInitFailed(name), BassAsio.LastError);

        BassAsio.CurrentDevice = device;
        var info = BassAsio.Info;
        return new AsioOutput(device, name, (int)Math.Round(BassAsio.Rate), info.Outputs);
    }

    /// <param name="cueMixer">0 = sin auriculares.</param>
    public void Start(int masterMixer, int masterChannel, int cueMixer, int cueChannel)
    {
        BassAsio.CurrentDevice = _device;
        EnablePair(masterMixer, masterChannel);
        if (cueMixer != 0)
            EnablePair(cueMixer, cueChannel);

        if (!BassAsio.Start(0, 0))
            throw new BassException(Messages.AsioStartFailed, BassAsio.LastError);
        _started = true;
        _masterChannel = masterChannel;

        LatencyFrames = Math.Max(0, BassAsio.GetLatency(false));
        double latencyMs = LatencyFrames * 1000.0 / SampleRate;
        MasterStatus = new AudioOutputStatus("ASIO", $"{_name} · {masterChannel + 1}-{masterChannel + 2}", SampleRate, 2, latencyMs);
        CueStatus = cueMixer != 0
            ? new AudioOutputStatus("ASIO", $"{_name} · {cueChannel + 1}-{cueChannel + 2}", SampleRate, 2, latencyMs)
            : null;
    }

    private void EnablePair(int mixer, int firstChannel)
    {
        if (firstChannel < 0 || firstChannel + 1 >= Outputs)
            throw new BassException(Messages.AsioChannelsMissing(firstChannel + 1, firstChannel + 2, Outputs), ManagedBass.Errors.Parameter);
        // join = true: los dos canales del mezclador (estéreo) van a firstChannel y firstChannel + 1.
        if (!BassAsio.ChannelEnableBass(false, firstChannel, mixer, true))
            throw new BassException(Messages.AsioChannelFailed(firstChannel + 1, firstChannel + 2), BassAsio.LastError);
    }

    private int _masterChannel = -1;

    /// <summary>Nivel de pico (0..1) del par de canales del master.</summary>
    public (float Left, float Right) GetMasterLevel()
    {
        if (_disposed || !_started || _masterChannel < 0)
            return (0, 0);
        BassAsio.CurrentDevice = _device;
        return ((float)Math.Max(0, BassAsio.ChannelGetLevel(false, _masterChannel)),
                (float)Math.Max(0, BassAsio.ChannelGetLevel(false, _masterChannel + 1)));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        BassAsio.CurrentDevice = _device;
        if (_started)
            BassAsio.Stop();
        BassAsio.Free();
    }
}
