using System.Windows;
using System.Windows.Controls;
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

    private (DeckViewModel Deck, bool IsCue)? MapKey(Key key) => ViewModel is not { } vm ? null : key switch
    {
        Key.Space => (vm.DeckA, false),
        Key.C => (vm.DeckA, true),
        Key.Enter => (vm.DeckB, false),
        Key.M => (vm.DeckB, true),
        _ => null,
    };

    private static bool IsTyping => Keyboard.FocusedElement is TextBox;

    // Un clic fuera del campo de enlace le quita el foco (para que vuelvan los atajos).
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (IsTyping && e.OriginalSource is DependencyObject source && FindAncestor<TextBox>(source) is null)
            Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Mientras se escribe un enlace, las teclas son para el campo de texto.
        if (IsTyping || MapKey(e.Key) is not var (deck, isCue))
            return;

        e.Handled = true;
        if (e.IsRepeat)
            return;

        if (isCue)
            deck.CueDownCommand.Execute(null);
        else
            deck.PlayPauseCommand.Execute(null);
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!IsTyping && MapKey(e.Key) is (var deck, true))
        {
            deck.CueUpCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }
}
