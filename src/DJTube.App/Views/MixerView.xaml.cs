using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DJTube.App.ViewModels;

namespace DJTube.App.Views;

public partial class MixerView : UserControl
{
    /// <summary>Ventana baja: mandos y crossfader más juntos para que el mezclador quepa entero.</summary>
    public static readonly DependencyProperty IsShortProperty = DependencyProperty.Register(
        nameof(IsShort), typeof(bool), typeof(MixerView), new PropertyMetadata(false));

    public MixerView()
    {
        InitializeComponent();
    }

    public bool IsShort
    {
        get => (bool)GetValue(IsShortProperty);
        set => SetValue(IsShortProperty, value);
    }

    private void OnCrossfaderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        (DataContext as MixerViewModel)?.CenterCrossfaderCommand.Execute(null);
        e.Handled = true;
    }
}
