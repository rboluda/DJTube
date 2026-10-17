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

        // Que quepa en pantallas pequeñas (1366×768, 1080p con escala…).
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel old)
                old.PropertyChanged -= OnViewModelChanged;
            if (e.NewValue is MainViewModel vm)
                vm.PropertyChanged += OnViewModelChanged;
            ApplyLibraryLayout();
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (ViewModel?.Recording is not { IsActive: true } recording || App.ExitingForUpdate)
            return;
        if (MessageBox.Show(this, Localization.Loc.T("Rec_ConfirmExit"), "DJTube",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            e.Cancel = true;
            return;
        }
        recording.StopForExit();
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowLibrary))
            ApplyLibraryLayout();
    }

    /// <summary>Muestra u oculta la biblioteca con el alto guardado (el divisor la redimensiona).</summary>
    private void ApplyLibraryLayout()
    {
        bool show = ViewModel?.ShowLibrary ?? false;
        LibraryPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LibrarySplitter.Visibility = LibraryPanel.Visibility;
        SplitterRow.Height = new GridLength(show ? 10 : 0);
        LibraryRow.MinHeight = show ? MainViewModel.MinLibraryHeight : 0;
        LibraryRow.Height = show ? new GridLength(FitLibraryHeight(ViewModel!.LibraryHeight)) : new GridLength(0);
    }

    /// <summary>Alto mínimo que necesitan platos y mezclador para verse enteros.</summary>
    private const double DecksMinHeight = 406;

    /// <summary>La biblioteca cede sitio a los platos si la ventana es baja (sin cambiar el alto guardado).</summary>
    private double FitLibraryHeight(double wanted)
    {
        double available = BodyGrid.ActualHeight - DecksMinHeight - SplitterRow.Height.Value;
        return available > 0 ? Math.Clamp(wanted, MainViewModel.MinLibraryHeight, Math.Max(MainViewModel.MinLibraryHeight, available)) : wanted;
    }

    private void OnBodySizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged)
            ApplyLibraryLayout();
    }

    private void OnLibrarySplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;
        vm.LibraryHeight = LibraryRow.ActualHeight;
        vm.SaveSettings();
    }

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
            case Key.L:
                vm.ToggleLibraryCommand.Execute(null);
                return true;
            case Key.H:
                ShowHistory();
                return true;
            default:
                return false;
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnMidiClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;
        ShowDialog(new MidiWindow { DataContext = vm.Midi });
        vm.Midi.StopLearning();
    }

    private HistoryWindow? _historyWindow;

    /// <summary>El historial es una ventana sin bloquear: se puede dejar abierta mientras se pincha.</summary>
    private void OnHistoryClick(object sender, RoutedEventArgs e) => ShowHistory();

    private void ShowHistory()
    {
        if (ViewModel is not { } vm)
            return;
        if (_historyWindow is { IsLoaded: true })
        {
            _historyWindow.Activate();
            return;
        }
        _historyWindow = new HistoryWindow { DataContext = new HistoryViewModel(vm.History), Owner = this, Icon = Icon };
        DarkTitleBar.Attach(_historyWindow);
        _historyWindow.Show();
    }

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
        dialog.Icon = Icon;
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
