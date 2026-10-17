using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DJTube.App.ViewModels;

namespace DJTube.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // La posición se lee del motor en cada fotograma; el audio no depende de esto.
        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnRendering(object? sender, EventArgs e) => ViewModel?.Refresh();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel?.DeckA is not { } deck || e.IsRepeat)
        {
            e.Handled = e.Key is Key.Space or Key.C;
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                deck.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.C:
                deck.CueDownCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && ViewModel?.DeckA is { } deck)
        {
            deck.CueUpCommand.Execute(null);
            e.Handled = true;
        }
    }
}
