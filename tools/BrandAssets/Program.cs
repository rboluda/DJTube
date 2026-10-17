using SkiaSharp;

// Uso: Brand <logo-original.png> <carpeta assets> <carpeta Assets de la app>
string input = args[0], assets = args[1], appAssets = args[2];
using var source = SKBitmap.Decode(input);
int w = source.Width, h = source.Height;

// 1) Paleta: colores medidos en zonas del logo.
var samples = new (string Name, int X, int Y)[]
{
    ("vinilo oscuro", 640, 260), ("vinilo medio", 760, 330), ("violeta brillo", 300, 760),
    ("violeta franja", 420, 860), ("amarillo arco", 470, 185), ("naranja arco", 230, 440),
    ("naranja borde", 1130, 640), ("rojo-naranja play", 690, 700), ("naranja play", 470, 520),
    ("aguja", 820, 830),
};
foreach (var (name, x, y) in samples)
{
    var c = Average(source, x, y, 6);
    Console.WriteLine($"{name,-18} #{c.Red:X2}{c.Green:X2}{c.Blue:X2}");
}

// 2) Fondo blanco → transparente. El brazo del tocadiscos también es blanco y toca el borde, así que
//    no vale un relleno por color: en cada dirección desde el centro se busca el borde naranja exterior
//    (el último píxel "con color") y solo se quita lo que queda fuera.
bool IsLight(SKColor c) => Math.Min(c.Red, Math.Min(c.Green, c.Blue)) >= 215
    && Math.Max(c.Red, Math.Max(c.Green, c.Blue)) - Math.Min(c.Red, Math.Min(c.Green, c.Blue)) <= 40;
// Píxel del borde naranja: mucho rojo y poco azul.
bool IsRim(SKColor c) => c.Red > 180 && c.Blue < 120;
double centerX = w / 2.0, centerY = h / 2.0;
{
    // Centro aproximado a partir de la caja de píxeles de borde.
    int bx0 = w, by0 = h, bx1 = 0, by1 = 0;
    for (int y = 0; y < h; y += 2)
        for (int x = 0; x < w; x += 2)
            if (!IsLight(source.GetPixel(x, y))) { bx0 = Math.Min(bx0, x); bx1 = Math.Max(bx1, x); by0 = Math.Min(by0, y); by1 = Math.Max(by1, y); }
    centerX = (bx0 + bx1) / 2.0; centerY = (by0 + by1) / 2.0;
}
const int Rays = 2880;
var rimRadius = new double[Rays];
for (int k = 0; k < Rays; k++)
{
    double angle = 2 * Math.PI * k / Rays, cos = Math.Cos(angle), sin = Math.Sin(angle);
    double last = 0;
    for (double r = 300; r < w; r += 0.5)
    {
        int x = (int)(centerX + r * cos), y = (int)(centerY + r * sin);
        if (x < 0 || y < 0 || x >= w || y >= h) break;
        if (IsRim(source.GetPixel(x, y))) last = r;
    }
    rimRadius[k] = last;
}
// Donde el brazo tapa el borde no se encuentra naranja: esas direcciones toman el radio típico.
double typical = rimRadius.Where(r => r > 0).Order().ElementAt(rimRadius.Count(r => r > 0) / 2);
for (int k = 0; k < Rays; k++)
    rimRadius[k] = Math.Max(rimRadius[k], typical * 0.97);
// Suavizado (mediana) para que ningún píxel suelto deforme el contorno.
var smoothRadius = new double[Rays];
for (int k = 0; k < Rays; k++)
{
    var window = Enumerable.Range(-6, 13).Select(d => rimRadius[(k + d + Rays) % Rays]).Order().ToArray();
    smoothRadius[k] = window[window.Length / 2];
}
double RimAt(int x, int y, out double distance)
{
    double dx = x + 0.5 - centerX, dy = y + 0.5 - centerY;
    distance = Math.Sqrt(dx * dx + dy * dy);
    double angle = Math.Atan2(dy, dx);
    if (angle < 0) angle += 2 * Math.PI;
    return smoothRadius[(int)(angle / (2 * Math.PI) * Rays) % Rays];
}
var background = new bool[w * h];
for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
        background[y * w + x] = RimAt(x, y, out double dist) + 1.0 < dist;

// Borde suavizado: los píxeles junto al fondo son mezcla de color y blanco; se estima su opacidad.
using var cut = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
int minX = w, minY = h, maxX = 0, maxY = 0;
for (int y = 0; y < h; y++)
{
    for (int x = 0; x < w; x++)
    {
        int i = y * w + x;
        if (background[i])
        {
            cut.SetPixel(x, y, SKColors.Transparent);
            continue;
        }
        var c = source.GetPixel(x, y);
        // Solo el anillo exterior (2 px junto al contorno) mezcla color con el blanco del fondo.
        bool nearBackground = RimAt(x, y, out double d) - d < 2.0;
        if (nearBackground)
        {
            // c = a·color + (1-a)·blanco; el naranja del borde tiene el azul casi a 0.
            double a = Math.Clamp((255.0 - c.Blue) / 235.0, 0, 1);
            if (a < 0.02) { cut.SetPixel(x, y, SKColors.Transparent); continue; }
            byte Un(byte v) => (byte)Math.Clamp((v - (1 - a) * 255) / a, 0, 255);
            c = new SKColor(Un(c.Red), Un(c.Green), Un(c.Blue), (byte)(a * 255));
        }
        cut.SetPixel(x, y, c);
        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
    }
}
Console.WriteLine($"recorte: x {minX}-{maxX}, y {minY}-{maxY}");

// 3) Cuadrado centrado con un pequeño margen.
int side = (int)(Math.Max(maxX - minX, maxY - minY) * 1.03);
int cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
using var square = new SKBitmap(new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul));
using (var canvas = new SKCanvas(square))
{
    canvas.Clear(SKColors.Transparent);
    using var img = SKImage.FromBitmap(cut);
    canvas.DrawImage(img, new SKRect(cx - side / 2f, cy - side / 2f, cx + side / 2f, cy + side / 2f),
        new SKRect(0, 0, side, side), new SKSamplingOptions(SKCubicResampler.Mitchell));
}

Save(Resize(square, 1024), Path.Combine(assets, "logo.png"));
Save(Resize(square, 256), Path.Combine(appAssets, "logo-256.png"));
Save(Resize(square, 64), Path.Combine(appAssets, "logo-64.png"));

// 4) Icono de Windows con PNG por tamaño.
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var images = sizes.Select(s => Encode(Resize(square, s))).ToList();
using (var ico = new BinaryWriter(File.Create(Path.Combine(appAssets, "djtube.ico"))))
{
    ico.Write((short)0); ico.Write((short)1); ico.Write((short)sizes.Length);
    int offset = 6 + 16 * sizes.Length;
    for (int k = 0; k < sizes.Length; k++)
    {
        int s = sizes[k];
        ico.Write((byte)(s >= 256 ? 0 : s)); ico.Write((byte)(s >= 256 ? 0 : s));
        ico.Write((byte)0); ico.Write((byte)0); ico.Write((short)1); ico.Write((short)32);
        ico.Write(images[k].Length); ico.Write(offset);
        offset += images[k].Length;
    }
    foreach (var data in images) ico.Write(data);
}
File.Copy(Path.Combine(appAssets, "djtube.ico"), Path.Combine(assets, "djtube.ico"), overwrite: true);
Console.WriteLine("listo");

static SKColor Average(SKBitmap b, int x, int y, int r)
{
    long R = 0, G = 0, B = 0, n = 0;
    for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++) { var c = b.GetPixel(x + dx, y + dy); R += c.Red; G += c.Green; B += c.Blue; n++; }
    return new SKColor((byte)(R / n), (byte)(G / n), (byte)(B / n));
}

static SKBitmap Resize(SKBitmap b, int size) =>
    b.Resize(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell))!;

static byte[] Encode(SKBitmap b)
{
    using var img = SKImage.FromBitmap(b);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

static void Save(SKBitmap b, string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, Encode(b));
}
