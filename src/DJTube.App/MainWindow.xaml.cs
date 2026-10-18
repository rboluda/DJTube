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
    /// <summary>Ventana baja: la vista ampliada, los platos y el mezclador usan menos alto para caber enteros
    /// junto a la biblioteca.</summary>
    public static readonly DependencyProperty IsShortProperty = DependencyProperty.Register(
        nameof(IsShort), typeof(bool), typeof(MainWindow),
        new PropertyMetadata(false, (d, _) => ((MainWindow)d).OnIsShortChanged()));

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
            UpdateShortLayout();
            ApplyLibraryLayout();
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public bool IsShort
    {
        get => (bool)GetValue(IsShortProperty);
        private set => SetValue(IsShortProperty, value);
    }

    /// <summary>La vista ampliada cambia de alto con el modo: la biblioteca se recoloca ya y otra vez tras medirse.</summary>
    private void OnIsShortChanged()
    {
        ApplyLibraryLayout();
        Dispatcher.BeginInvoke(ApplyLibraryLayout, System.Windows.Threading.DispatcherPriority.Loaded);
    }

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
        {
            UpdateShortLayout();
            ApplyLibraryLayout();
        }
    }

    /// <summary>Muestra u oculta la biblioteca con el alto guardado (el divisor la redimensiona).</summary>
    private void ApplyLibraryLayout()
    {
        bool show = ViewModel?.ShowLibrary ?? false;
        LibraryPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LibrarySplitter.Visibility = LibraryPanel.Visibility;
        SplitterRow.Height = new GridLength(show ? SplitterHeight : 0);
        DecksRow.MinHeight = DecksHeight;
        LibraryRow.MinHeight = show ? MainViewModel.MinLibraryHeight : 0;
        LibraryRow.Height = show ? new GridLength(FitLibraryHeight(ViewModel!.LibraryHeight)) : new GridLength(0);
    }

    /// <summary>Alto mínimo que necesitan platos y mezclador para verse enteros.</summary>
    private const double DecksMinHeight = 406;

    /// <summary>Lo mismo con la ventana baja (<see cref="IsShort"/>).</summary>
    private const double DecksShortMinHeight = 300;

    private const double SplitterHeight = 10;

    private double DecksHeight => IsShort ? DecksShortMinHeight : DecksMinHeight;

    /// <summary>Alto de lo que rodea a platos y biblioteca (salidas, vista ampliada, grabación) con la vista
    /// normal; se mide en ese modo y sirve para saber si la vista normal cabría.</summary>
    private double _normalChromeHeight;

    /// <summary>Alto de las barras del panel que no son platos ni biblioteca.</summary>
    private double ChromeHeight()
    {
        double used = 0;
        foreach (FrameworkElement child in Shell.Children)
            if (child != BodyGrid && child.Visibility != Visibility.Collapsed)
                used += child.ActualHeight + child.Margin.Top + child.Margin.Bottom;
        return used;
    }

    /// <summary>Alto que queda para platos y biblioteca. No vale BodyGrid.ActualHeight: si su contenido no cabe,
    /// WPF lo coloca a su alto deseado y lo recorta (y la biblioteca queda fuera de la vista).</summary>
    private double BodyHeight => Shell.ActualHeight - ChromeHeight();

    /// <summary>La biblioteca cede sitio a los platos si la ventana es baja (sin cambiar el alto guardado).</summary>
    private double FitLibraryHeight(double wanted)
    {
        double available = BodyHeight - DecksHeight - SplitterRow.Height.Value;
        return available > 0 ? Math.Clamp(wanted, MainViewModel.MinLibraryHeight, Math.Max(MainViewModel.MinLibraryHeight, available)) : wanted;
    }

    private void OnBodySizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged)
            ApplyLibraryLayout();
    }

    private void OnShellSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged)
            return;
        UpdateShortLayout();
        ApplyLibraryLayout();
    }

    /// <summary>Pasa a la vista baja si con la normal no cabrían los platos y el mínimo de la biblioteca.
    /// Se decide con el alto total (que no depende del modo) para no oscilar entre las dos vistas.</summary>
    private void UpdateShortLayout()
    {
        if (Shell.ActualHeight <= 0)
            return;
        if (!IsShort)
            _normalChromeHeight = ChromeHeight();
        bool library = ViewModel?.ShowLibrary ?? false;
        double needed = DecksMinHeight + (library ? SplitterHeight + MainViewModel.MinLibraryHeight : 0);
        IsShort = Shell.ActualHeight - _normalChromeHeight < needed;
    }

    private void OnLibrarySplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;
        vm.LibraryHeight = LibraryRow.ActualHeight;
        vm.SaveSettings();
    }

    private void OnRendering(object? sender, EventArgs e) => ViewModel?.Refresh();

    /// <summary>Acción de la tecla según los atajos (personalizables en Preferencias → Teclado).</summary>
    private string? ActionFor(KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
            return null;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return KeyboardShortcuts.Gesture(key, Keyboard.Modifiers) is { } gesture
            && vm.KeyBindings.TryGetValue(gesture, out var action) ? action : null;
    }

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
        if (IsTyping || ActionFor(e) is not { } action)
            return;

        e.Handled = true;
        if (!e.IsRepeat)
            vm.Midi.Trigger(action, press: true);
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        // Soltar importa en CUE y en el loop roll (mientras se mantiene).
        if (!IsTyping && ViewModel is { } vm && ActionFor(e) is { } action)
        {
            vm.Midi.Trigger(action, press: false);
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
