using DJTube.Core.Localization;
using ManagedBass;
using ManagedBass.Fx;

namespace DJTube.Audio.Bass;

// El espacio de nombres DJTube.Audio.Bass taparía a la clase ManagedBass.Bass.
using Bass = ManagedBass.Bass;

/// <summary>
/// Inicialización global de BASS (una vez por proceso).
/// </summary>
internal static class BassRuntime
{
    private static readonly Lock Gate = new();
    private static bool _initialized;

    /// <summary>Handle del plugin FLAC, o 0 si no se pudo cargar.</summary>
    public static int FlacPlugin { get; private set; }

    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
                return;

            // Fuerza la carga de bass_fx.dll antes de crear streams de tempo.
            _ = BassFx.Version;

            // BASS no reproduce nada directamente: todo sale por mezcladores "decode" que
            // leen BASSWASAPI/BASSASIO. No hace falta su hilo de actualización.
            Bass.Configure(Configuration.UpdatePeriod, 0);
            Bass.Configure(Configuration.FloatDSP, true);

            // Dispositivo 0 = "sin sonido": suficiente para decodificar y mezclar.
            if (!Bass.Init(0, 48000, DeviceInitFlags.Default) && Bass.LastError != Errors.Already)
                throw new BassException(Messages.BassInitFailed);

            FlacPlugin = Bass.PluginLoad(Path.Combine(AppContext.BaseDirectory, "bassflac.dll"));

            _initialized = true;
        }
    }
}
