using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DJTube.App.Services;

/// <summary>Barra de título oscura o clara (Windows 10 20H1+ y Windows 11), a juego con el tema.</summary>
public static partial class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Se aplica al crear la ventana (antes de mostrarla).</summary>
    public static void Attach(Window window) => window.SourceInitialized += (_, _) => Update(window);

    /// <summary>Vuelve a aplicar el color de la barra (al cambiar de tema).</summary>
    public static void Update(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;
        int enabled = ThemeManager.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }
}
