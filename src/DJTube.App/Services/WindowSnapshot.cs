using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DJTube.App.Services;

/// <summary>Guarda el contenido de una ventana como PNG (sin pasar por la pantalla).</summary>
public static class WindowSnapshot
{
    public static void Save(Window window, string path)
    {
        if (window.Content is not FrameworkElement content)
            return;
        var dpi = VisualTreeHelper.GetDpi(content);
        int width = (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY);
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

        // Fondo de la ventana (el contenido no lo pinta) y después el contenido.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            // Recuadro exacto del contenido (sin estirar: algo que sobresalga no debe deformar la imagen).
            var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            var brush = new VisualBrush(content)
            {
                Stretch = Stretch.None,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = bounds,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            dc.DrawRectangle(brush, null, bounds);
        }
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
