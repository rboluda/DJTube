using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DJTube.App.ViewModels;

namespace DJTube.App.Views;

public partial class LibraryView : UserControl
{
    private Point? _dragStart;
    private bool _editRequested;

    public LibraryView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LibraryViewModel old)
                old.PropertyChanged -= OnViewModelChanged;
            if (e.NewValue is LibraryViewModel vm)
                vm.PropertyChanged += OnViewModelChanged;
        };
        // Si el DataGrid se mide antes de tener ancho (biblioteca oculta, ventana arrancando…), las columnas
        // proporcionales se quedan con el mínimo: se reparten de nuevo al cargar y al cambiar de ancho.
        LibraryGrid.Loaded += (_, _) => ResetStarColumnsLater();
        LibraryGrid.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
                ResetStarColumnsLater();
        };
    }

    private void ResetStarColumnsLater() =>
        Dispatcher.BeginInvoke(ResetStarColumns, System.Windows.Threading.DispatcherPriority.Loaded);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.IsGrouped))
            ResetStarColumnsLater();
    }

    /// <summary>
    /// Al activar o quitar la agrupación, el DataGrid de WPF deja las columnas proporcionales (Artista,
    /// Título) con el ancho mínimo; volver a asignarles su ancho hace que se repartan otra vez.
    /// </summary>
    private void ResetStarColumns()
    {
        foreach (var column in LibraryGrid.Columns)
        {
            if (!column.Width.IsStar)
                continue;
            var width = column.Width;
            column.Width = new DataGridLength(0);
            column.Width = width;
        }
    }

    private LibraryViewModel? ViewModel => DataContext as LibraryViewModel;

    // Solo se edita con F2 o desde el menú: un clic en una fila ya seleccionada no debe abrir el editor.
    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (!_editRequested)
            e.Cancel = true;
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (LibraryGrid.IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox)
            return; // editando: Intro y Esc son del editor

        switch (e.Key)
        {
            case Key.F2:
                BeginEdit();
                e.Handled = true;
                break;
            case Key.Delete:
                ViewModel?.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e) => BeginEdit();

    private void BeginEdit()
    {
        if (LibraryGrid.SelectedItem is not { } item)
            return;
        LibraryGrid.CurrentCell = new DataGridCellInfo(item, ArtistColumn);
        _editRequested = true;
        try
        {
            LibraryGrid.BeginEdit();
        }
        finally
        {
            _editRequested = false;
        }
    }

    // Ordenar por columna dentro de cada rama (el árbol no se deshace).
    private void OnSorting(object? sender, DataGridSortingEventArgs e)
    {
        if (ViewModel is not { } vm || string.IsNullOrEmpty(e.Column.SortMemberPath))
            return;
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        foreach (var column in LibraryGrid.Columns)
            column.SortDirection = null;
        e.Column.SortDirection = direction;
        vm.SortBy(e.Column.SortMemberPath, direction);
    }

    // Ctrl + rueda: tamaño de letra (más pequeña = más canciones a la vista).
    private void OnGridMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || ViewModel is not { } vm)
            return;
        if (e.Delta > 0)
            vm.FontLargerCommand.Execute(null);
        else
            vm.FontSmallerCommand.Execute(null);
        e.Handled = true;
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not DataGridRow { Item: LibraryEntry entry })
            return;
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return; // doble clic en los botones A/B
        ViewModel?.LoadToFreeCommand.Execute(entry);
        e.Handled = true;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            ViewModel?.CloseResultsCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // Intro: buscar en YouTube (la biblioteca ya se filtra mientras se escribe).
            ViewModel?.SearchYouTubeCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not DataGridRow { Item: SearchResult result })
            return;
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;
        ViewModel?.ResultLoadFreeCommand.Execute(result);
        e.Handled = true;
    }

    private void OnImportKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel?.ImportCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ImportBox.Text = "";
            e.Handled = true;
        }
    }

    // Soltar un enlace (de una canción o una lista) en la biblioteca lo importa.
    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool fromHere = e.Data.GetDataPresent(typeof(LibraryEntry));
        e.Effects = !fromHere && e.Data.GetDataPresent(DataFormats.UnicodeText) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(LibraryEntry)) || e.Data.GetData(DataFormats.UnicodeText) is not string text
            || ViewModel is not { } vm)
            return;
        e.Handled = true;
        await vm.ImportAsync(text.Trim());
    }

    // Arrastrar una fila a un plato: se lleva el enlace de YouTube como texto (el plato ya acepta enlaces).
    private void OnGridMouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        bool onRow = FindAncestor<DataGridRow>(source) is not null && FindAncestor<Button>(source) is null;
        _dragStart = onRow ? e.GetPosition(this) : null;
    }

    private void OnGridMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            _dragStart = null;
            return;
        }
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragStart = null;
        var grid = sender as DataGrid ?? LibraryGrid;
        string? link = grid.SelectedItem switch
        {
            LibraryEntry entry when !IsEditing => entry.Track.Link,
            SearchResult result => result.Link,
            _ => null,
        };
        if (link is not null)
        {
            var data = new DataObject(DataFormats.UnicodeText, link);
            data.SetData(typeof(LibraryEntry), grid.SelectedItem); // para no importarla al soltarla en la biblioteca
            DragDrop.DoDragDrop(grid, data, DragDropEffects.Copy);
        }
    }

    private bool IsEditing => Keyboard.FocusedElement is TextBox box && FindAncestor<DataGrid>(box) == LibraryGrid;

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }
}
