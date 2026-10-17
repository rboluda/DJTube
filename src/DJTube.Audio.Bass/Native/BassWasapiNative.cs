using System.Runtime.InteropServices;
using ManagedBass.Wasapi;

namespace DJTube.Audio.Bass.Native;

/// <summary>
/// Llamadas a basswasapi.dll que ManagedBass no expone.
/// </summary>
internal static partial class BassWasapiNative
{
    /// <summary>
    /// WASAPIPROC_BASS: BASSWASAPI lee directamente de un canal BASS (el handle va en <c>user</c>).
    /// Así el hilo de audio nunca ejecuta código .NET.
    /// </summary>
    private static readonly IntPtr WasapiProcBass = new(-1);

    [LibraryImport("basswasapi", EntryPoint = "BASS_WASAPI_Init")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Init(
        int device, int freq, int chans, WasapiInitFlags flags, float buffer, float period, IntPtr proc, IntPtr user);

    /// <summary>Inicializa <paramref name="device"/> alimentado por el canal BASS <paramref name="channel"/> (decode).</summary>
    public static bool InitFromChannel(
        int device, int freq, int chans, WasapiInitFlags flags, float bufferSeconds, int channel) =>
        Init(device, freq, chans, flags, bufferSeconds, 0f, WasapiProcBass, new IntPtr(channel));
}
