namespace DJTube.Core.Logic;

public static class TimeFormat
{
    /// <summary>Formatea segundos como "m:ss.d" (décimas truncadas). Negativos, NaN e infinito se muestran como 0.</summary>
    public static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;

        long tenths = (long)Math.Floor(seconds * 10 + 1e-9);
        long minutes = tenths / 600;
        long secs = tenths / 10 % 60;
        long tenth = tenths % 10;
        return $"{minutes}:{secs:00}.{tenth}";
    }
}
