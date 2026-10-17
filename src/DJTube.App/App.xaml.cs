using System.Windows;
using DJTube.App.Services;
using DJTube.App.ViewModels;
using DJTube.Audio.Bass;
using DJTube.Sources.Local;

namespace DJTube.App;

public partial class App : Application
{
    private BassAudioEngine? _engine;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _engine = new BassAudioEngine(deckCount: 2);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo iniciar el motor de audio:\n\n{ex.Message}",
                "DJTube", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var viewModel = new MainViewModel(_engine, new LocalFileTrackSource(), new FileDialogService());
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
        viewModel.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engine?.Dispose();
        base.OnExit(e);
    }
}
