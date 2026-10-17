using System.Windows;
using DJTube.App.ViewModels;

namespace DJTube.App.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as HistoryViewModel)?.Detach();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
