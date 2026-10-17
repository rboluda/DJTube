using System.IO;
using System.Text.Json;

namespace DJTube.App.Services;

/// <summary>Preferencias que se recuerdan entre sesiones.</summary>
public sealed class AppSettings
{
    /// <summary>"es" o "en"; null = según el idioma de Windows.</summary>
    public string? Language { get; set; }

    /// <summary>Nombre del dispositivo de salida (los índices cambian al conectar o quitar dispositivos).</summary>
    public string? OutputDeviceName { get; set; }

    public double ZoomSeconds { get; set; } = 8;

    public string? CrossfaderCurve { get; set; }

    /// <summary>"Wasapi" o "Asio".</summary>
    public string? OutputMode { get; set; }

    /// <summary>Dispositivo WASAPI de auriculares; null = sin pre-escucha.</summary>
    public string? CueDeviceName { get; set; }

    public string? AsioDeviceName { get; set; }

    /// <summary>Primer canal ASIO del master (0 = canales 1-2).</summary>
    public int AsioMasterChannel { get; set; }

    /// <summary>Primer canal ASIO de auriculares (2 = canales 3-4); -1 = sin pre-escucha.</summary>
    public int AsioCueChannel { get; set; } = 2;

    public double CueMix { get; set; }

    public double HeadphoneVolume { get; set; } = 0.8;

    /// <summary>Buscar una versión nueva en GitHub al arrancar.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Biblioteca visible bajo los platos.</summary>
    public bool ShowLibrary { get; set; } = true;

    public double LibraryHeight { get; set; } = 220;

    /// <summary>Carpeta de las grabaciones (null = Música\DJTube).</summary>
    public string? RecordingsFolder { get; set; }
}

/// <summary>Lee y guarda <see cref="AppSettings"/> en %APPDATA%\DJTube\settings.json.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsService(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DJTube", "settings.json");
    }

    public string Path { get; }

    /// <summary>Lee las preferencias; si no hay o están dañadas, devuelve las de por defecto.</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            string tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No poder guardar las preferencias no debe interrumpir la sesión.
        }
    }
}
