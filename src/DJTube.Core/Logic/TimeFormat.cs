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

    /// <summary>Formatea una duración como "hh:mm:ss" (segundos truncados), para la grabación.</summary>
    public static string Clock(TimeSpan time)
    {
        long total = time > TimeSpan.Zero ? (long)Math.Floor(time.TotalSeconds + 1e-9) : 0;
        return $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}";
    }
}
