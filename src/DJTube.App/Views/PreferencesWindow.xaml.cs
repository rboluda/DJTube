using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DJTube.App.ViewModels;

namespace DJTube.App.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    private PreferencesViewModel? ViewModel => DataContext as PreferencesViewModel;

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;

    // Cancelar (o cerrar) deshace la vista previa del tema.
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DialogResult != true)
            ViewModel?.Cancel();
    }

    // Editor de atajos: la tecla pulsada con una fila en espera se asigna a esa acción.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (ViewModel?.TryCaptureKey(key, Keyboard.Modifiers) == true)
            e.Handled = true;
    }
}
