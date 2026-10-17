using System.IO;

namespace DJTube.App.Services;

/// <summary>Registro de errores inesperados en %LOCALAPPDATA%\DJTube\errors.log.</summary>
public static class ErrorLog
{
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DJTube", "errors.log");

    public static void Write(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DJTube {AppInfo.Version}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
