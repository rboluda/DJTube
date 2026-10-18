using System.Windows.Input;

namespace DJTube.App.Services;

/// <summary>
/// Atajos de teclado de las acciones de los platos (personalizables). Las teclas se guardan como texto
/// ("Space", "Shift+D3") y las acciones son las mismas que las de las controladoras MIDI.
/// </summary>
public static class KeyboardShortcuts
{
    /// <summary>Atajos de serie.</summary>
    public static IReadOnlyDictionary<string, string> Defaults { get; } = BuildDefaults();

    private static Dictionary<string, string> BuildDefaults()
    {
        var map = new Dictionary<string, string>
        {
            ["Space"] = "DeckA.PlayPause",
            ["C"] = "DeckA.Cue",
            ["Return"] = "DeckB.PlayPause",
            ["M"] = "DeckB.Cue",
            ["Q"] = "DeckA.Loop",
            ["W"] = "DeckA.Reloop",
            ["S"] = "DeckA.Sync",
            ["P"] = "DeckB.Loop",
            ["O"] = "DeckB.Reloop",
            ["K"] = "DeckB.Sync",
        };
        for (int i = 1; i <= 8; i++)
        {
            map[$"D{i}"] = $"DeckA.HotCue{i}";
            map[$"Shift+D{i}"] = $"DeckB.HotCue{i}";
        }
        return map;
    }

    /// <summary>Texto de una tecla con sus modificadores (Ctrl y Alt no se usan: son de los menús).</summary>
    public static string? Gesture(Key key, ModifierKeys modifiers)
    {
        if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.System or Key.LWin or Key.RWin || modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Alt))
            return null;
        return modifiers.HasFlag(ModifierKeys.Shift) ? $"Shift+{key}" : key.ToString();
    }

    /// <summary>Cómo se ve una tecla en pantalla: "D3" → "3", "Shift+D3" → "Mayús+3".</summary>
    public static string Display(string gesture, string shiftName)
    {
        string text = gesture.Replace("Shift+", shiftName + "+", StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.Replace(text, @"\bD(\d)\b", "$1")
            .Replace("Return", "Enter", StringComparison.Ordinal);
    }
}
