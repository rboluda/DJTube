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

    /// <summary>Se cierra para instalar una actualización (sin preguntar por la grabación: ya se avisó).</summary>
    public static bool ExitingForUpdate { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Pase lo que pase, la grabación en curso se cierra bien (la cabecera del WAV queda completa).
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ErrorLog.Write(ex);
            StopRecordingQuietly();
        };
        SessionEnding += (_, _) => StopRecordingQuietly();
        // Un error en la interfaz no debe cerrar el programa (ni cortar la música): se registra y se avisa.
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write(args.Exception);
            MessageBox.Show(args.Exception.Message, "DJTube", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        Controls.SliderWheel.Register();

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

    private void StopRecordingQuietly()
    {
        try
        {
            _viewModel?.Recording.StopForExit();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopRecordingQuietly();
        _viewModel?.SaveSettings();
        _engine?.Dispose();
        base.OnExit(e);
    }
}
