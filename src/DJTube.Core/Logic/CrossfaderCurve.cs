namespace DJTube.Core.Logic;

public enum CrossfaderCurveType
{
    /// <summary>Fundido lineal de amplitud: en el centro cada plato a la mitad.</summary>
    Linear,

    /// <summary>Corte rápido (scratch): ambos platos a tope salvo en los extremos.</summary>
    FastCut,

    /// <summary>Potencia constante: fundido suave sin bajón de volumen en el centro.</summary>
    ConstantPower,
}

/// <summary>Ganancias de los platos A y B según la posición del crossfader.</summary>
public readonly record struct CrossfaderGains(double A, double B);

public static class CrossfaderCurve
{
    /// <summary>Recorrido (fracción del total, en cada extremo) en el que corta la curva rápida.</summary>
    public const double FastCutWidth = 0.05;

    /// <param name="position">-1 = todo A, 0 = centro, +1 = todo B. Fuera de rango se limita.</param>
    public static CrossfaderGains Gains(double position, CrossfaderCurveType curve)
    {
        double x = (Math.Clamp(double.IsNaN(position) ? 0 : position, -1, 1) + 1) / 2; // 0 = A, 1 = B
        return curve switch
        {
            CrossfaderCurveType.Linear => new CrossfaderGains(1 - x, x),
            CrossfaderCurveType.FastCut => new CrossfaderGains(
                Math.Clamp((1 - x) / FastCutWidth, 0, 1),
                Math.Clamp(x / FastCutWidth, 0, 1)),
            CrossfaderCurveType.ConstantPower => new CrossfaderGains(
                Math.Cos(x * Math.PI / 2),
                Math.Sin(x * Math.PI / 2)),
            _ => throw new ArgumentOutOfRangeException(nameof(curve), curve, null),
        };
    }
}
