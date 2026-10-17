using System.Reflection;
using System.Runtime.InteropServices;

namespace DJTube.App.Services;

/// <summary>Versión y datos de compilación del programa.</summary>
public static class AppInfo
{
    public const string ProjectUrl = "https://github.com/rboluda/DJTube";

    // "0.4.0+<commit de git>" (el SDK añade el commit al compilar dentro de un repositorio).
    private static readonly string InformationalVersion =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>Versión semántica, p. ej. "0.4.0".</summary>
    public static string Version => InformationalVersion.Split('+')[0];

    /// <summary>Commit corto de la compilación, si se conoce.</summary>
    public static string? Commit
    {
        get
        {
            int plus = InformationalVersion.IndexOf('+');
            if (plus < 0 || plus == InformationalVersion.Length - 1)
                return null;
            string commit = InformationalVersion[(plus + 1)..];
            return commit.Length > 7 ? commit[..7] : commit;
        }
    }

    public static string Runtime => RuntimeInformation.FrameworkDescription;

    public static string Title => $"DJTube {Version}";
}
