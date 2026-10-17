using System.Globalization;

namespace DJTube.Core.Updates;

/// <summary>
/// Versión semántica sencilla: mayor.menor.parche con sufijo opcional de pre-versión ("-beta.1").
/// Admite la "v" de las etiquetas de git ("v0.7.0") y descarta los metadatos de compilación ("+abc123").
/// </summary>
public sealed record SemanticVersion(int Major, int Minor, int Patch, string? PreRelease = null)
    : IComparable<SemanticVersion>
{
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];
        int plus = s.IndexOf('+');
        if (plus >= 0)
            s = s[..plus];

        string? pre = null;
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..];
            s = s[..dash];
            if (pre.Length == 0)
                return false;
        }

        string[] parts = s.Split('.');
        if (parts.Length is < 1 or > 3)
            return false;
        var numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Versión no válida: {text}");

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
            return 1;
        int c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0)
            return c;

        // Una versión final es mayor que cualquiera de sus pre-versiones (1.0.0 > 1.0.0-beta).
        if (PreRelease is null || other.PreRelease is null)
            return (PreRelease is null ? 1 : 0) - (other.PreRelease is null ? 1 : 0);
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;

    public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;

    public override string ToString() => PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    /// <summary>Por identificadores separados por puntos: los numéricos se comparan como números.</summary>
    private static int ComparePreRelease(string a, string b)
    {
        string[] x = a.Split('.'), y = b.Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            bool xn = int.TryParse(x[i], out int xi), yn = int.TryParse(y[i], out int yi);
            int c = (xn, yn) switch
            {
                (true, true) => xi.CompareTo(yi),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(x[i], y[i]),
            };
            if (c != 0)
                return c;
        }
        return x.Length.CompareTo(y.Length);
    }
}
