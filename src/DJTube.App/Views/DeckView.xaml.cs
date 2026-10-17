using System.Windows;
using System.Windows.Controls;
using DJTube.App.ViewModels;

namespace DJTube.App.Views;

public partial class DeckView : UserControl
{
    public DeckView()
    {
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is DeckViewModel deck
            && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            await deck.LoadFileAsync(files[0]);
        }
    }
}
