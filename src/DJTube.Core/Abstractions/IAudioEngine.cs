using DJTube.Core.Models;

namespace DJTube.Core.Abstractions;

public interface IAudioEngine : IDisposable
{
    IReadOnlyList<IDeck> Decks { get; }

    /// <summary>Estado de la salida, o null si no está arrancada.</summary>
    AudioOutputStatus? OutputStatus { get; }

    IReadOnlyList<AudioDevice> GetOutputDevices();

    /// <summary>(Re)arranca la salida de audio. Los platos conservan su pista, posición y estado.</summary>
    void StartOutput(AudioOutputSettings settings);

    void StopOutput();
}
