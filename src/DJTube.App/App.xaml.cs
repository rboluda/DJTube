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

        // --snapshot archivo.png [segundos]: dibuja la ventana en un PNG y se cierra (capturas para la
        // documentación; funciona aunque la sesión de Windows esté bloqueada).
        int snapshot = Array.IndexOf(e.Args, "--snapshot");
        if (snapshot >= 0 && snapshot + 1 < e.Args.Length)
        {
            double delay = snapshot + 2 < e.Args.Length && double.TryParse(e.Args[snapshot + 2],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s) ? s : 4;
            _ = SnapshotAndExitAsync(window, e.Args[snapshot + 1], delay);
        }
    }

    private async Task SnapshotAndExitAsync(Window window, string path, double delaySeconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        try
        {
            Services.WindowSnapshot.Save(window, path);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
        Shutdown();
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
        _viewModel?.Midi.Dispose();
        _viewModel?.SaveSettings();
        _engine?.Dispose();
        base.OnExit(e);
    }
}
