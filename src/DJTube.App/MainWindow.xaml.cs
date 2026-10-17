using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DJTube.App.Services;
using DJTube.App.ViewModels;
using DJTube.App.Views;

namespace DJTube.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Attach(this);
        // La posición se lee del motor en cada fotograma; el audio no depende de esto.
        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnRendering(object? sender, EventArgs e) => ViewModel?.Refresh();

    private (DeckViewModel Deck, bool IsCue)? MapDeckKey(Key key) => ViewModel is not { } vm ? null : key switch
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
        if (ViewModel is not { } vm)
            return;

        if (HandleGlobalShortcut(vm, e))
        {
            e.Handled = true;
            return;
        }

        // Mientras se escribe un enlace, las teclas son para el campo de texto.
        if (IsTyping || Keyboard.Modifiers != ModifierKeys.None || MapDeckKey(e.Key) is not var (deck, isCue))
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
        if (!IsTyping && MapDeckKey(e.Key) is (var deck, true))
        {
            deck.CueUpCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Atajos que funcionan siempre (también escribiendo un enlace).</summary>
    private bool HandleGlobalShortcut(MainViewModel vm, KeyEventArgs e)
    {
        if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ShowDialog(new ShortcutsWindow());
            return true;
        }
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return false;

        switch (e.Key)
        {
            case Key.D1 or Key.NumPad1:
                vm.DeckA.LoadFileCommand.Execute(null);
                return true;
            case Key.D2 or Key.NumPad2:
                vm.DeckB.LoadFileCommand.Execute(null);
                return true;
            case Key.OemComma:
                ShowPreferences();
                return true;
            case Key.OemPlus or Key.Add:
                vm.ZoomInCommand.Execute(null);
                return true;
            case Key.OemMinus or Key.Subtract:
                vm.ZoomOutCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnPreferencesClick(object sender, RoutedEventArgs e) => ShowPreferences();

    private void OnShortcutsClick(object sender, RoutedEventArgs e) => ShowDialog(new ShortcutsWindow());

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            ShowDialog(new AboutWindow { DataContext = new AboutViewModel(vm) });
    }

    private void ShowPreferences()
    {
        if (ViewModel is not { } vm)
            return;
        var preferences = new PreferencesViewModel(vm);
        if (ShowDialog(new PreferencesWindow { DataContext = preferences }) == true)
            preferences.Apply();
    }

    private bool? ShowDialog(Window dialog)
    {
        dialog.Owner = this;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DarkTitleBar.Attach(dialog);
        bool? result = dialog.ShowDialog();
        Focus();
        return result;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }
}
