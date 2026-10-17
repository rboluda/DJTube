using System.Windows;
using DJTube.App.ViewModels;

namespace DJTube.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
