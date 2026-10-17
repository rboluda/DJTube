using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using System.Windows.Markup;

namespace DJTube.App.Localization;

public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Textos de la interfaz en el idioma elegido (Resources/Strings*.resx). Cambiar de idioma
/// actualiza al momento todo lo enlazado con <see cref="TrExtension"/> y avisa con
/// <see cref="LanguageChanged"/> para que los modelos de vista recalculen sus textos.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string Spanish = "es";
    public const string English = "en";

    private static readonly ResourceManager Resources =
        new("DJTube.App.Resources.Strings", typeof(Loc).Assembly);

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    /// <summary>Idiomas disponibles (con su nombre en ese idioma).</summary>
    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(Spanish, "Español"),
        new(English, "English"),
    ];

    public CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    public string Language => Culture.TwoLetterISOLanguageName == Spanish ? Spanish : English;

    public string this[string key] => Resources.GetString(key, Culture) ?? $"[{key}]";

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args) => string.Format(Instance.Culture, Instance[key], args);

    /// <summary>Español si Windows está en español; inglés en cualquier otro caso.</summary>
    public static string DefaultLanguage =>
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == Spanish ? Spanish : English;

    public void SetLanguage(string? code)
    {
        var culture = CultureInfo.GetCultureInfo(code == Spanish ? "es-ES" : "en-US");
        if (Equals(culture, Culture) && Equals(CultureInfo.CurrentUICulture, culture))
            return;

        Culture = culture;
        // Las librerías (mensajes de error) usan la cultura de interfaz del hilo.
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Texto traducido en XAML: <c>Text="{loc:Tr Clave}"</c>. Se actualiza al cambiar de idioma.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
