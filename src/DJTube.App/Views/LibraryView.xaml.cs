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

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not DataGridRow { Item: LibraryItem item })
            return;
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return; // doble clic en los botones A/B
        ViewModel?.LoadToFreeCommand.Execute(item);
        e.Handled = true;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
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
        if (LibraryGrid.SelectedItem is LibraryItem item && !IsEditing)
            DragDrop.DoDragDrop(LibraryGrid,new DataObject(DataFormats.UnicodeText, item.Link), DragDropEffects.Copy);
    }

    private bool IsEditing => Keyboard.FocusedElement is TextBox box && FindAncestor<DataGrid>(box) == LibraryGrid;

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }
}
