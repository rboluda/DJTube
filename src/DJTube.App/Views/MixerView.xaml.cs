using System.Windows.Controls;
using System.Windows.Input;
using DJTube.App.ViewModels;

namespace DJTube.App.Views;

public partial class MixerView : UserControl
{
    public MixerView()
    {
        InitializeComponent();
    }

    private void OnCrossfaderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        (DataContext as MixerViewModel)?.CenterCrossfaderCommand.Execute(null);
        e.Handled = true;
    }
}
