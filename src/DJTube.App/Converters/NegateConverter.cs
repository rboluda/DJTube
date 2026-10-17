using System.Globalization;
using System.Windows.Data;

namespace DJTube.App.Converters;

/// <summary>x → -x (para el mínimo del pitch fader a partir del rango).</summary>
public sealed class NegateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? -d : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? -d : value;
}
