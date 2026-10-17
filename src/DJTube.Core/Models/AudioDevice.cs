namespace DJTube.Core.Models;

/// <summary>Dispositivo de salida de audio. <paramref name="Id"/> es opaco y depende del motor.</summary>
public sealed record AudioDevice(int Id, string Name, bool IsDefault)
{
    public override string ToString() => IsDefault ? $"{Name} (predeterminado)" : Name;
}

/// <param name="DeviceId">Dispositivo de salida; null = el predeterminado de Windows.</param>
/// <param name="BufferMilliseconds">Tamaño de buffer pedido; 0 = el mínimo que admita el dispositivo.</param>
public sealed record AudioOutputSettings(int? DeviceId = null, double BufferMilliseconds = 0);

public sealed record AudioOutputStatus(string DeviceName, int SampleRate, int Channels, double LatencyMilliseconds);
