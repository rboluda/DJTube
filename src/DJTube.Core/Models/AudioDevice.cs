using DJTube.Core.Localization;

namespace DJTube.Core.Models;

/// <summary>Dispositivo de salida de audio. <paramref name="Id"/> es opaco y depende del motor.</summary>
public sealed record AudioDevice(int Id, string Name, bool IsDefault)
{
    public override string ToString() => IsDefault ? Messages.DefaultDevice(Name) : Name;
}

/// <summary>Cómo sale el audio: master y (opcional) auriculares para la pre-escucha.</summary>
public abstract record OutputConfig;

/// <summary>
/// WASAPI: master y auriculares en dispositivos distintos (p. ej. altavoces y la salida de cascos).
/// </summary>
/// <param name="MasterDeviceId">Dispositivo master; null = el predeterminado de Windows.</param>
/// <param name="CueDeviceId">Dispositivo de auriculares; null = sin pre-escucha.</param>
/// <param name="BufferMilliseconds">Tamaño de buffer pedido; 0 = el mínimo que admita el dispositivo.</param>
public sealed record WasapiOutputConfig(int? MasterDeviceId = null, int? CueDeviceId = null, double BufferMilliseconds = 0)
    : OutputConfig
{
    /// <summary>
    /// <see cref="CueDeviceId"/> especial: auriculares en la misma salida que el master, en modo "split"
    /// (izquierda = master en mono, derecha = pre-escucha en mono). Para quien solo tiene una salida.
    /// </summary>
    public const int SplitCue = -2;
}

/// <summary>
/// ASIO: una tarjeta multicanal; master y auriculares en pares de canales (0 = canales 1-2, 2 = canales 3-4…).
/// </summary>
/// <param name="CueChannel">Primer canal del par de auriculares; -1 = sin pre-escucha.</param>
public sealed record AsioOutputConfig(int DeviceId, int MasterChannel = 0, int CueChannel = 2) : OutputConfig;

/// <param name="Driver">"WASAPI" o "ASIO".</param>
public sealed record AudioOutputStatus(
    string Driver, string DeviceName, int SampleRate, int Channels, double LatencyMilliseconds);
