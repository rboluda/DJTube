using System.IO;

namespace DJTube.App.Services;

/// <summary>
/// Carpetas de datos de DJTube. Por defecto %APPDATA%\DJTube (preferencias), %LOCALAPPDATA%\DJTube (caché,
/// listas, hot cues, errores) y Música\DJTube (grabaciones). Con la variable de entorno DJTUBE_DATA todo va a
/// esa carpeta: sirve para pruebas o capturas sin tocar los datos habituales.
/// </summary>
public static class AppFolders
{
    private static readonly string? Override = Environment.GetEnvironmentVariable("DJTUBE_DATA") is { Length: > 0 } dir
        ? Path.GetFullPath(dir)
        : null;

    /// <summary>Preferencias (settings.json, midi.json).</summary>
    public static string Roaming { get; } = Override
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DJTube");

    /// <summary>Caché, listas, hot cues y registro de errores.</summary>
    public static string Local { get; } = Override
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DJTube");

    /// <summary>Carpeta de grabaciones si no se ha elegido otra.</summary>
    public static string Recordings { get; } = Override is not null
        ? Path.Combine(Override, "Grabaciones")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "DJTube");
}
