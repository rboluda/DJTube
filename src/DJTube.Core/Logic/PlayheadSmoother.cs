namespace DJTube.Core.Logic;

/// <summary>
/// Suaviza la posición de reproducción para dibujar. El motor la da a saltos (por bloques de mezcla
/// de ~10 ms); a 60 fotogramas por segundo eso se ve como tirones en la vista ampliada.
/// Se predice con el reloj y la velocidad, y se corrige poco a poco hacia lo que dice el motor.
/// </summary>
public sealed class PlayheadSmoother
{
    /// <summary>Si la predicción se aleja más que esto, se salta directamente (seek, carga, etc.).</summary>
    public const double SnapThresholdSeconds = 0.05;

    private const double CorrectionGain = 0.15;

    private double _position;
    private double _time;
    private bool _hasState;

    /// <param name="reportedPosition">Posición que da el motor.</param>
    /// <param name="nowSeconds">Reloj monotónico.</param>
    /// <param name="rate">Velocidad de reproducción (1 = normal).</param>
    public double Update(double reportedPosition, double nowSeconds, double rate, bool playing)
    {
        if (!playing || !_hasState)
            return Reset(reportedPosition, nowSeconds, playing);

        double predicted = _position + (nowSeconds - _time) * rate;
        double error = reportedPosition - predicted;
        double result = Math.Abs(error) > SnapThresholdSeconds ? reportedPosition : predicted + error * CorrectionGain;

        _position = result;
        _time = nowSeconds;
        return result;
    }

    private double Reset(double position, double now, bool playing)
    {
        _position = position;
        _time = now;
        _hasState = playing;
        return position;
    }
}
