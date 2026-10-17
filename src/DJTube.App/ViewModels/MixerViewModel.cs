using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Localization;
using DJTube.Core.Abstractions;
using DJTube.Core.Logic;

namespace DJTube.App.ViewModels;

/// <summary>
/// Faders de canal y crossfader (ganancia de cada plato hacia el master) y sección de auriculares:
/// CUE por plato, mezcla CUE/master y volumen.
/// </summary>
public partial class MixerViewModel : ObservableObject
{
    private readonly IAudioEngine _engine;
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

    /// <summary>Plato A por los auriculares.</summary>
    [ObservableProperty]
    private bool _cueA;

    [ObservableProperty]
    private bool _cueB;

    /// <summary>0 = auriculares solo con los platos en CUE; 1 = lo mismo que el master.</summary>
    [ObservableProperty]
    private double _cueMix;

    [ObservableProperty]
    private double _headphoneVolume;

    /// <summary>Mando de volumen master (0..1).</summary>
    [ObservableProperty]
    private double _masterVolume;

    [ObservableProperty]
    private double _masterLevelLeft;

    [ObservableProperty]
    private double _masterLevelRight;

    public MixerViewModel(IAudioEngine engine, CrossfaderCurveType curve, double cueMix, double headphoneVolume, double masterVolume = 1)
    {
        _masterVolume = Math.Clamp(masterVolume, 0, 1);
        engine.MasterVolume = FaderTaper.ToGain(_masterVolume);
        _engine = engine;
        _deckA = engine.Decks[0];
        _deckB = engine.Decks[1];
        EqA = new ChannelEqViewModel(_deckA);
        EqB = new ChannelEqViewModel(_deckB);
        _selectedCurve = Curves.FirstOrDefault(c => c.Value == curve) ?? Curves[0];
        _cueMix = Math.Clamp(cueMix, 0, 1);
        _headphoneVolume = Math.Clamp(headphoneVolume, 0, 1);
        engine.CueMix = _cueMix;
        engine.HeadphoneVolume = FaderTaper.ToGain(_headphoneVolume);
        ApplyGains();
        Loc.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CueATip));
            OnPropertyChanged(nameof(CueBTip));
            OnPropertyChanged(nameof(EqResetATip));
            OnPropertyChanged(nameof(EqResetBTip));
        };
    }

    public IReadOnlyList<LocalizedOption<CrossfaderCurveType>> Curves { get; } =
    [
        new(CrossfaderCurveType.Linear, _ => Loc.T("Curve_Linear")),
        new(CrossfaderCurveType.FastCut, _ => Loc.T("Curve_FastCut")),
        new(CrossfaderCurveType.ConstantPower, _ => Loc.T("Curve_ConstantPower")),
    ];

    /// <summary>Ganancia, EQ, filtro y VU del canal A.</summary>
    public ChannelEqViewModel EqA { get; }

    public ChannelEqViewModel EqB { get; }

    /// <summary>Se llama en cada fotograma (medidores).</summary>
    public void Refresh()
    {
        EqA.RefreshLevel();
        EqB.RefreshLevel();
        var (left, right) = _engine.GetMasterLevel();
        MasterLevelLeft = Follow(MasterLevelLeft, EqMath.MeterFraction(left));
        MasterLevelRight = Follow(MasterLevelRight, EqMath.MeterFraction(right));
    }

    private static double Follow(double shown, double target) => target >= shown ? target : Math.Max(target, shown - 0.012);

    partial void OnMasterVolumeChanged(double value) => _engine.MasterVolume = FaderTaper.ToGain(value);

    public string CueATip => Loc.F("PflTip", "A");

    public string CueBTip => Loc.F("PflTip", "B");

    public string EqResetATip => Loc.F("Eq_ResetTip", "A");

    public string EqResetBTip => Loc.F("Eq_ResetTip", "B");

    partial void OnVolumeAChanged(double value) => ApplyGains();

    partial void OnVolumeBChanged(double value) => ApplyGains();

    partial void OnCrossfaderChanged(double value) => ApplyGains();

    partial void OnSelectedCurveChanged(LocalizedOption<CrossfaderCurveType> value) => ApplyGains();

    partial void OnCueAChanged(bool value) => _deckA.CueEnabled = value;

    partial void OnCueBChanged(bool value) => _deckB.CueEnabled = value;

    partial void OnCueMixChanged(double value) => _engine.CueMix = value;

    partial void OnHeadphoneVolumeChanged(double value) => _engine.HeadphoneVolume = FaderTaper.ToGain(value);

    [RelayCommand]
    private void CenterCrossfader() => Crossfader = 0;

    private void ApplyGains()
    {
        var gains = CrossfaderCurve.Gains(Crossfader, SelectedCurve.Value);
        _deckA.MasterGain = FaderTaper.ToGain(VolumeA) * gains.A;
        _deckB.MasterGain = FaderTaper.ToGain(VolumeB) * gains.B;
    }
}
