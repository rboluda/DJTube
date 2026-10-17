using DJTube.Core.Models;

namespace DJTube.Core.Abstractions;

public interface IAudioEngine : IDisposable
{
    IReadOnlyList<IDeck> Decks { get; }

    /// <summary>Estado de la salida master, o null si no está arrancada.</summary>
    AudioOutputStatus? OutputStatus { get; }

    /// <summary>Estado de la salida de auriculares, o null si no hay pre-escucha.</summary>
    AudioOutputStatus? CueOutputStatus { get; }

    /// <summary>Por qué no arrancó la salida de auriculares (el master sigue sonando), o null.</summary>
    string? CueOutputError { get; }

    /// <summary>Audio ya mezclado que aún no se ha oído en el master, en segundos.</summary>
    double OutputDelaySeconds { get; }

    /// <summary>Mezcla de auriculares: 0 = solo los platos con CUE, 1 = lo mismo que el master.</summary>
    double CueMix { get; set; }

    /// <summary>Volumen de auriculares (0..1).</summary>
    double HeadphoneVolume { get; set; }

    /// <summary>Dispositivos WASAPI de salida.</summary>
    IReadOnlyList<AudioDevice> GetOutputDevices();

    /// <summary>Controladores ASIO instalados.</summary>
    IReadOnlyList<AudioDevice> GetAsioDevices();

    /// <summary>(Re)arranca la salida. Los platos conservan su pista, posición y estado.</summary>
    void StartOutput(OutputConfig config);

    void StopOutput();

    /// <summary>
    /// Mantenimiento periódico (unas decenas de veces por segundo, desde la interfaz): mantiene la
    /// ruta de auriculares sincronizada con el master cuando cada uno tiene su propio reloj.
    /// </summary>
    void Maintain();
}
