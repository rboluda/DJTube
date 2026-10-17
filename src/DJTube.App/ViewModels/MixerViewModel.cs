using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Localization;
using DJTube.Core.Abstractions;
using DJTube.Core.Logic;

namespace DJTube.App.ViewModels;

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
    private LocalizedOption<CrossfaderCurveType> _selectedCurve;

    public MixerViewModel(IDeck deckA, IDeck deckB, CrossfaderCurveType curve = CrossfaderCurveType.Linear)
    {
        _deckA = deckA;
        _deckB = deckB;
        _selectedCurve = Curves.FirstOrDefault(c => c.Value == curve) ?? Curves[0];
        ApplyGains();
    }

    public IReadOnlyList<LocalizedOption<CrossfaderCurveType>> Curves { get; } =
    [
        new(CrossfaderCurveType.Linear, _ => Loc.T("Curve_Linear")),
        new(CrossfaderCurveType.FastCut, _ => Loc.T("Curve_FastCut")),
        new(CrossfaderCurveType.ConstantPower, _ => Loc.T("Curve_ConstantPower")),
    ];

    partial void OnVolumeAChanged(double value) => ApplyGains();

    partial void OnVolumeBChanged(double value) => ApplyGains();

    partial void OnCrossfaderChanged(double value) => ApplyGains();

    partial void OnSelectedCurveChanged(LocalizedOption<CrossfaderCurveType> value) => ApplyGains();

    [RelayCommand]
    private void CenterCrossfader() => Crossfader = 0;

    private void ApplyGains()
    {
        var gains = CrossfaderCurve.Gains(Crossfader, SelectedCurve.Value);
        _deckA.MasterGain = FaderTaper.ToGain(VolumeA) * gains.A;
        _deckB.MasterGain = FaderTaper.ToGain(VolumeB) * gains.B;
    }
}
