namespace DJTube.Core.Logic;

/// <summary>
/// Color de la onda según sus frecuencias (como en los programas de DJ): graves en rojo, medios en
/// verde y agudos en azul, mezclados según lo que domina en cada instante. Un bombo sale rojo,
/// una voz verde y unos platillos azules.
/// </summary>
public static class WaveformColors
{
    public static readonly (byte R, byte G, byte B) LowColor = (255, 48, 40);
    public static readonly (byte R, byte G, byte B) MidColor = (60, 225, 90);
    public static readonly (byte R, byte G, byte B) HighColor = (70, 160, 255);

    /// <summary>Mezcla de los tres colores ponderada por la energía (amplitud²) de cada banda.</summary>
    public static (byte R, byte G, byte B) Mix(float low, float mid, float high)
    {
        double wl = Square(low), wm = Square(mid), wh = Square(high);
        double sum = wl + wm + wh;
        if (sum <= 0)
            return MidColor;

        byte Channel(byte l, byte m, byte h) => (byte)Math.Round((wl * l + wm * m + wh * h) / sum);
        return (
            Channel(LowColor.R, MidColor.R, HighColor.R),
            Channel(LowColor.G, MidColor.G, HighColor.G),
            Channel(LowColor.B, MidColor.B, HighColor.B));
    }

    private static double Square(float v) => double.IsFinite(v) && v > 0 ? (double)v * v : 0;
}
