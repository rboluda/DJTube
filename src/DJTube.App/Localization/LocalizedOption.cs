using CommunityToolkit.Mvvm.ComponentModel;

namespace DJTube.App.Localization;

/// <summary>Opción de un desplegable cuyo texto depende del idioma (se actualiza al cambiarlo).</summary>
public sealed class LocalizedOption<T> : ObservableObject
{
    private readonly Func<T, string> _label;

    public LocalizedOption(T value, Func<T, string> label)
    {
        Value = value;
        _label = label;
        Loc.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public T Value { get; }

    public string Label => _label(Value);

    public override string ToString() => Label;
}
