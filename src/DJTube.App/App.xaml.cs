using System.Windows;
using DJTube.App.Localization;
using DJTube.App.Services;
using DJTube.App.ViewModels;
using DJTube.Audio.Bass;
using DJTube.Core.Analysis;
using DJTube.Sources.Local;
using DJTube.Sources.YouTube;

namespace DJTube.App;

public partial class App : Application
{
    private BassAudioEngine? _engine;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Un error en la interfaz no debe cerrar el programa (ni cortar la música): se registra y se avisa.
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write(args.Exception);
            MessageBox.Show(args.Exception.Message, "DJTube", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        Loc.Instance.SetLanguage(settings.Language ?? Loc.DefaultLanguage);

        try
        {
            _engine = new BassAudioEngine(deckCount: 2);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Loc.F("EngineStartError", Environment.NewLine, ex.Message),
                "DJTube", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // El análisis (onda y BPM) se guarda para que volver a cargar una canción sea instantáneo.
        string analysisCache = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DJTube", "Cache", "Analysis");
        _viewModel = new MainViewModel(
            _engine,
            new LocalFileTrackSource(),
            new YouTubeTrackSource(),
            new CachingTrackAnalyzer(new BassTrackAnalyzer(), analysisCache),
            new FileDialogService(),
            settingsService,
            settings);
        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();
        _viewModel.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.SaveSettings();
        _engine?.Dispose();
        base.OnExit(e);
    }
}
