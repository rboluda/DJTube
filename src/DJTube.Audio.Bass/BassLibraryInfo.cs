using ManagedBass;
using ManagedBass.Asio;
using ManagedBass.Fx;
using ManagedBass.Mix;
using ManagedBass.Wasapi;

namespace DJTube.Audio.Bass;

/// <summary>
/// Comprueba que las DLL nativas de BASS se cargan y devuelve sus versiones.
/// </summary>
public static class BassLibraryInfo
{
    public static IReadOnlyList<(string Library, string Version)> GetVersions()
    {
        var result = new List<(string, string)>();
        Add(result, "bass", () => ManagedBass.Bass.Version);
        Add(result, "bass_fx", () => BassFx.Version);
        Add(result, "bassmix", () => BassMix.Version);
        Add(result, "basswasapi", () => BassWasapi.Version);
        Add(result, "bassasio", () => BassAsio.Version);
        return result;
    }

    private static void Add(List<(string, string)> list, string name, Func<Version> getVersion)
    {
        try
        {
            list.Add((name, getVersion().ToString()));
        }
        catch (DllNotFoundException)
        {
            list.Add((name, "NO ENCONTRADA"));
        }
    }
}
