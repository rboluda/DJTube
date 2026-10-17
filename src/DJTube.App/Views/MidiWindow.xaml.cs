using System.Windows;

namespace DJTube.App.Views;

public partial class MidiWindow : Window
{
    public MidiWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
