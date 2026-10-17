namespace DJTube.Core.Models;

/// <summary>
/// Tonalidad de una canción: tónica (0 = Do … 11 = Si) y modo. Se muestra con la notación Camelot de los
/// DJ (1A–12A menores, 1B–12B mayores): dos canciones encajan si tienen el mismo número, o uno más o
/// menos con la misma letra, o el mismo número con la otra letra.
/// </summary>
public readonly record struct MusicalKey(int Tonic, bool Minor)
{
    private static readonly string[] NoteNames = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    // Número Camelot de cada tónica (índice = tónica). Mayores: Do = 8B; menores: La = 8A.
    private static readonly int[] MajorCamelot = [8, 3, 10, 5, 12, 7, 2, 9, 4, 11, 6, 1];
    private static readonly int[] MinorCamelot = [5, 12, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10];

    public int CamelotNumber => (Minor ? MinorCamelot : MajorCamelot)[Mod12(Tonic)];

    public string Camelot => $"{CamelotNumber}{(Minor ? 'A' : 'B')}";

    /// <summary>Nombre con la notación inglesa habitual en DJ: "Am", "C", "F#m".</summary>
    public string Name => NoteNames[Mod12(Tonic)] + (Minor ? "m" : "");

    /// <summary>Encaje armónico: misma tonalidad, ±1 en la rueda, o relativa mayor/menor.</summary>
    public bool IsCompatibleWith(MusicalKey other)
    {
        int a = CamelotNumber, b = other.CamelotNumber;
        if (Minor == other.Minor)
        {
            int distance = Math.Abs(a - b);
            return distance is 0 or 1 or 11;
        }
        return a == b;
    }

    /// <summary>La tonalidad que suena con el pitch aplicado (sin key lock): cada semitono mueve la tónica.</summary>
    public MusicalKey Transpose(int semitones) => this with { Tonic = Mod12(Tonic + semitones) };

    /// <summary>Lee "8A" / "12B".</summary>
    public static bool TryParseCamelot(string? text, out MusicalKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
            return false;
        char letter = char.ToUpperInvariant(text[^1]);
        if (letter is not ('A' or 'B') || !int.TryParse(text[..^1], out int number) || number is < 1 or > 12)
            return false;
        bool minor = letter == 'A';
        int tonic = Array.IndexOf(minor ? MinorCamelot : MajorCamelot, number);
        key = new MusicalKey(tonic, minor);
        return true;
    }

    /// <summary>Código compacto para guardar: 0–11 mayores, 12–23 menores.</summary>
    public int Code => Mod12(Tonic) + (Minor ? 12 : 0);

    public static MusicalKey FromCode(int code) => new(Mod12(code), code >= 12);

    private static int Mod12(int v) => ((v % 12) + 12) % 12;
}
