using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DJTube.Core.Logic;

namespace DJTube.App.ViewModels;

/// <summary>Un botón de hot cue: vacío, o con un punto de la pista al que saltar.</summary>
public partial class HotCueSlot : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSet))]
    private double? _position;

    public HotCueSlot(int index)
    {
        Index = index;
        var (r, g, b) = HotCueMath.Colors[index];
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        Fill = brush;
    }

    public int Index { get; }

    public string Number => (Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public Brush Fill { get; }

    public bool IsSet => Position is not null;
}
