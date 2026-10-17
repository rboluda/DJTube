using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DJTube.App.Services;

/// <summary>Barra de título oscura en Windows 10 20H1+ y Windows 11, a juego con el tema.</summary>
public static partial class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Se aplica al crear la ventana (antes de mostrarla).</summary>
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            int enabled = 1;
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        };
    }
}
