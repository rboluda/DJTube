using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.Core.Abstractions;
using DJTube.Core.Logic;

namespace DJTube.App.ViewModels;

/// <summary>Mandos de un canal del mezclador (ganancia, agudos, medios, graves, filtro) y su VU.</summary>
public partial class ChannelEqViewModel : ObservableObject
{
    /// <summary>Caída del VU por fotograma (posición 0..1): unos 35 dB por segundo a 60 fps.</summary>
    private const double MeterFallPerFrame = 0.012;

    private readonly IDeck _deck;

    [ObservableProperty]
    private double _gain;

    [ObservableProperty]
    private double _high;

    [ObservableProperty]
    private double _mid;

    [ObservableProperty]
    private double _low;

    [ObservableProperty]
    private double _filter;

    [ObservableProperty]
    private double _levelLeft;

    [ObservableProperty]
    private double _levelRight;

    public ChannelEqViewModel(IDeck deck)
    {
        _deck = deck;
        var eq = deck.Eq;
        (_gain, _high, _mid, _low, _filter) = (eq.Gain, eq.High, eq.Mid, eq.Low, eq.Filter);
    }

    public string GainText => $"{EqMath.GainDb(Gain):+0.0;-0.0;0.0} dB";

    public string HighText => EqMath.FormatBand(High);

    public string MidText => EqMath.FormatBand(Mid);

    public string LowText => EqMath.FormatBand(Low);

    public string FilterText => EqMath.Filter(Filter) switch
    {
        { Kind: FilterKind.LowPass, CutoffHz: var hz } => $"LPF {FormatHz(hz)}",
        { Kind: FilterKind.HighPass, CutoffHz: var hz } => $"HPF {FormatHz(hz)}",
        _ => "OFF",
    };

    partial void OnGainChanged(double value) => Apply(nameof(GainText));

    partial void OnHighChanged(double value) => Apply(nameof(HighText));

    partial void OnMidChanged(double value) => Apply(nameof(MidText));

    partial void OnLowChanged(double value) => Apply(nameof(LowText));

    partial void OnFilterChanged(double value) => Apply(nameof(FilterText));

    /// <summary>Todo al centro.</summary>
    [RelayCommand]
    private void Reset() => (Gain, High, Mid, Low, Filter) = (0, 0, 0, 0, 0);

    /// <summary>Se llama en cada fotograma: sube al instante y cae despacio, como un VU de mezclador.</summary>
    public void RefreshLevel()
    {
        var (left, right) = _deck.GetLevel();
        LevelLeft = Follow(LevelLeft, EqMath.MeterFraction(left));
        LevelRight = Follow(LevelRight, EqMath.MeterFraction(right));
    }

    private static double Follow(double shown, double target) =>
        target >= shown ? target : Math.Max(target, shown - MeterFallPerFrame);

    private void Apply(string textProperty)
    {
        _deck.Eq = new EqSettings(Gain, High, Mid, Low, Filter);
        OnPropertyChanged(textProperty);
    }

    private static string FormatHz(double hz) => hz >= 1000 ? $"{hz / 1000:0.0} kHz" : $"{hz:0} Hz";
}
