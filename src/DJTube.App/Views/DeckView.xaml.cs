using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DJTube.App.ViewModels;

namespace DJTube.App.Views;

public partial class DeckView : UserControl
{
    /// <summary>Color del plato (cabecera, onda, BPM, interruptores activos).</summary>
    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
        nameof(AccentColor), typeof(Color), typeof(DeckView),
        new PropertyMetadata(Colors.DodgerBlue, (d, e) => ((DeckView)d).Accent = Frozen(new SolidColorBrush((Color)e.NewValue))));

    /// <summary>Pincel de <see cref="AccentColor"/> (para las propiedades que piden Brush).</summary>
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(DeckView), new PropertyMetadata(Brushes.DodgerBlue));

    private const double PitchWheelStep = 0.02;

    public DeckView()
    {
        InitializeComponent();
    }

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        private set => SetValue(AccentProperty, value);
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private DeckViewModel? ViewModel => DataContext as DeckViewModel;

    // Se aceptan archivos del explorador y enlaces arrastrados desde el navegador (llegan como texto).
    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool accepted = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText);
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { } deck)
            return;
        e.Handled = true;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await deck.LoadFileAsync(files[0]);
        else if (e.Data.GetData(DataFormats.UnicodeText) is string text)
            await deck.LoadTextAsync(text);
    }

    private void OnLinkKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LoadLink();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ReleaseKeyboard();
            e.Handled = true;
        }
    }

    private void OnLoadLinkClick(object sender, RoutedEventArgs e) => LoadLink();

    private void LoadLink()
    {
        ViewModel?.LoadLinkCommand.Execute(null);
        ReleaseKeyboard();
    }

    /// <summary>Saca el foco del campo de enlace para que vuelvan a funcionar los atajos de teclado.</summary>
    private void ReleaseKeyboard() => Window.GetWindow(this)?.Focus();

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
