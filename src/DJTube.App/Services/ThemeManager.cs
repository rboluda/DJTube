using System.Windows;
using System.Windows.Media;

namespace DJTube.App.Services;

/// <summary>
/// Temas de color: cambia el diccionario de colores de la aplicación en caliente (los estilos los usan como
/// recursos dinámicos) y avisa a los controles que pintan por su cuenta (ondas, mandos, medidores).
/// </summary>
public static class ThemeManager
{
    public const string Default = "Violet";

    /// <summary>Temas disponibles (nombre interno = sufijo de Themes/Colors.*.xaml).</summary>
    public static IReadOnlyList<string> Names { get; } = ["Violet", "Light", "Win98", "Blue"];

    public static string Current { get; private set; } = Default;

    /// <summary>Cambia con cada tema (para invalidar lo que se haya pintado y guardado).</summary>
    public static int Version { get; private set; }

    public static event EventHandler? Changed;

    public static bool IsDark => Application.Current?.TryFindResource("ThemeIsDark") is not false;

    public static void Apply(string? name)
    {
        if (name is null || !Names.Contains(name))
            name = Default;
        var app = Application.Current;
        if (app is null)
            return;

        var dictionaries = app.Resources.MergedDictionaries;
        var colors = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/Colors.{name}.xaml") };
        if (dictionaries.Count > 0 && dictionaries[0].Source?.OriginalString.Contains("/Colors.", StringComparison.Ordinal) == true)
            dictionaries[0] = colors;
        else
            dictionaries.Insert(0, colors);

        Current = name;
        Version++;
        foreach (Window window in app.Windows)
            DarkTitleBar.Update(window);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Un color del tema activo (para lo que se pinta a mano).</summary>
    public static Color GetColor(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) is Color c ? c : fallback;
}
