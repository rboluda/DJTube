using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DJTube.App.ViewModels;

namespace DJTube.App.Views;

public partial class DeckView : UserControl
{
    /// <summary>Color del plato (cabecera, progreso, interruptores activos).</summary>
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(DeckView), new PropertyMetadata(Brushes.DodgerBlue));

    private const double PitchWheelStep = 0.02;

    public DeckView()
    {
        InitializeComponent();
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private DeckViewModel? ViewModel => DataContext as DeckViewModel;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is { } deck && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await deck.LoadFileAsync(files[0]);
    }

    private void OnPitchDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ResetPitchCommand.Execute(null);
        e.Handled = true;
    }

    // Rueda hacia arriba = más rápido.
    private void OnPitchWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel?.NudgePitch(Math.Sign(e.Delta) * PitchWheelStep);
        e.Handled = true;
    }
}
