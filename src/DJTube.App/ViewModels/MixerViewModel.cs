using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.Core.Abstractions;
using DJTube.Core.Logic;

namespace DJTube.App.ViewModels;

public sealed record CrossfaderCurveOption(CrossfaderCurveType Type, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Faders de canal y crossfader: calculan la ganancia de cada plato hacia el master.</summary>
public partial class MixerViewModel : ObservableObject
{
    private readonly IDeck _deckA;
    private readonly IDeck _deckB;

    /// <summary>Fader de canal A (0..1).</summary>
    [ObservableProperty]
    private double _volumeA = 1;

    [ObservableProperty]
    private double _volumeB = 1;

    /// <summary>-1 = solo A, +1 = solo B.</summary>
    [ObservableProperty]
    private double _crossfader;

    [ObservableProperty]
    private CrossfaderCurveOption _selectedCurve;

    public MixerViewModel(IDeck deckA, IDeck deckB)
    {
        _deckA = deckA;
        _deckB = deckB;
        _selectedCurve = Curves[0];
        ApplyGains();
    }

    public IReadOnlyList<CrossfaderCurveOption> Curves { get; } =
    [
        new(CrossfaderCurveType.Linear, "Lineal"),
        new(CrossfaderCurveType.FastCut, "Corte rápido"),
        new(CrossfaderCurveType.ConstantPower, "Suave (potencia constante)"),
    ];

    partial void OnVolumeAChanged(double value) => ApplyGains();

    partial void OnVolumeBChanged(double value) => ApplyGains();

    partial void OnCrossfaderChanged(double value) => ApplyGains();

    partial void OnSelectedCurveChanged(CrossfaderCurveOption value) => ApplyGains();

    [RelayCommand]
    private void CenterCrossfader() => Crossfader = 0;

    private void ApplyGains()
    {
        var gains = CrossfaderCurve.Gains(Crossfader, SelectedCurve.Type);
        _deckA.MasterGain = FaderTaper.ToGain(VolumeA) * gains.A;
        _deckB.MasterGain = FaderTaper.ToGain(VolumeB) * gains.B;
    }
}
