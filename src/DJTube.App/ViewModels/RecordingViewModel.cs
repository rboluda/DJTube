using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Localization;
using DJTube.App.Services;
using DJTube.Core.Abstractions;
using DJTube.Core.Logic;
using DJTube.Core.Recording;

namespace DJTube.App.ViewModels;

/// <summary>Barra de grabación: grabar la sesión (lo que suena por el master), pausar y detener.</summary>
public partial class RecordingViewModel : ObservableObject
{
    /// <summary>Por debajo de este espacio libre se avisa (unos 50 minutos de WAV a 48 kHz).</summary>
    private const long LowSpaceBytes = 1L << 30;

    private const double MeterFallPerFrame = 0.015;

    private readonly SessionRecorder _recorder;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private string _elapsedText = "00:00:00";
    private long _lastSizeUpdate;
    private (string Key, object? Arg)? _notice;

    [ObservableProperty]
    private double _level;

    [ObservableProperty]
    private bool _isLowSpace;

    [ObservableProperty]
    private string _sizeText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isExporting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private double _exportProgress;

    private RecorderState _lastState;
    private CancellationTokenSource? _exportCts;
    private string? _exportResult; // texto tras convertir (o el error)

    /// <summary>Historial de la sesión: su lista de temas se guarda junto a cada grabación.</summary>
    public SessionHistory? History { get; set; }

    /// <summary>Tiempo grabado (para colocar los temas en la grabación).</summary>
    public TimeSpan RecordedTime => _recorder.Recorded;

    public RecordingFormat Format =>
        Enum.TryParse(_settings.RecordingFormat, out RecordingFormat f) ? f : RecordingFormat.Mp3;

    public RecordingViewModel(IAudioEngine engine, AppSettings settings, Action saveSettings)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        _recorder = new SessionRecorder(engine.OpenMasterTap);
        _recorder.StateChanged += (_, _) => Application.Current?.Dispatcher.InvokeAsync(OnRecorderChanged);
        Loc.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(NoticeText));
        };
    }

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "DJTube");

    /// <summary>Carpeta de grabaciones (Preferencias).</summary>
    public string Folder
    {
        get => string.IsNullOrWhiteSpace(_settings.RecordingsFolder) ? DefaultFolder : _settings.RecordingsFolder;
        set
        {
            _settings.RecordingsFolder = string.Equals(value, DefaultFolder, StringComparison.OrdinalIgnoreCase) ? null : value;
            _saveSettings();
            OnPropertyChanged();
        }
    }

    public RecorderState State => _recorder.State;

    public bool IsRecording => State == RecorderState.Recording;

    public bool IsPaused => State == RecorderState.Paused;

    /// <summary>Grabando o en pausa (hay un archivo abierto).</summary>
    public bool IsActive => State != RecorderState.Stopped;

    public string ElapsedText => _elapsedText;

    public string FileName => _recorder.CurrentFile is { } path ? Path.GetFileName(path) : "";

    public string StatusText => State switch
    {
        RecorderState.Recording => Loc.T("Rec_Recording"),
        RecorderState.Paused => Loc.T("Rec_Paused"),
        _ when _recorder.Error is { } error => Loc.F("Rec_Error", error),
        _ when IsExporting => Loc.F("Rec_Exporting", AudioExporter.Extension(Format).TrimStart('.').ToUpperInvariant(), ExportProgress * 100),
        _ when _exportResult is { } result => result,
        _ when _recorder.CurrentFile is not null => Loc.T("Rec_Saved"),
        _ => Loc.T("Rec_Ready"),
    };

    [RelayCommand]
    private void CancelExport() => _exportCts?.Cancel();

    /// <summary>
    /// Al terminar una grabación: guarda la lista de temas (.txt y .cue) y la convierte al formato elegido.
    /// </summary>
    private async Task FinishRecordingAsync(IReadOnlyList<string> files)
    {
        _exportResult = null;
        WriteTracklist(files[0]);
        var format = Format;
        if (format == RecordingFormat.Wav)
            return;

        _exportCts = new CancellationTokenSource();
        IsExporting = true;
        ExportProgress = 0;
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                int index = i;
                await AudioExporter.ExportAsync(files[i], format,
                    new Progress<double>(p => ExportProgress = (index + p) / files.Count), _exportCts.Token);
            }
            _exportResult = Loc.F("Rec_SavedAs", AudioExporter.Extension(format).TrimStart('.').ToUpperInvariant());
        }
        catch (OperationCanceledException)
        {
            _exportResult = Loc.T("Rec_ExportCancelled");
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            _exportResult = Loc.F("Rec_ExportError", ex.Message);
        }
        finally
        {
            IsExporting = false;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private void WriteTracklist(string recording)
    {
        if (History?.RecordingEntries is not { Count: > 0 } entries)
            return;
        try
        {
            string name = Path.GetFileNameWithoutExtension(recording);
            File.WriteAllText(Path.ChangeExtension(recording, ".txt"),
                name + Environment.NewLine + Environment.NewLine + SessionHistory.ToText(entries, useRecordingTime: true));
            File.WriteAllText(Path.ChangeExtension(recording, ".cue"),
                SessionHistory.ToCueSheet(Path.GetFileName(recording), name, entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
        }
    }

    public bool HasError => !IsActive && _recorder.Error is not null;

    /// <summary>Aviso puntual (grabaciones recuperadas tras un cierre inesperado).</summary>
    public string NoticeText => _notice is { } n ? (n.Arg is null ? Loc.T(n.Key) : Loc.F(n.Key, n.Arg)) : "";

    public bool HasNotice => _notice is not null;

    /// <summary>Al arrancar: arregla las grabaciones que quedaron a medias por un cierre inesperado.</summary>
    public void RecoverInterrupted()
    {
        var recovered = SessionRecorder.RecoverInterrupted(Folder);
        if (recovered.Count == 0)
            return;
        _notice = recovered.Count == 1
            ? ("Rec_Recovered", Path.GetFileName(recovered[0]))
            : ("Rec_RecoveredMany", recovered.Count);
        OnPropertyChanged(nameof(NoticeText));
        OnPropertyChanged(nameof(HasNotice));
    }

    /// <summary>Grabar (o seguir, si está en pausa).</summary>
    [RelayCommand]
    private void Record()
    {
        if (IsPaused)
        {
            _recorder.Resume();
            return;
        }
        if (IsActive)
            return;
        try
        {
            DismissNotice();
            _exportResult = null;
            _recorder.Start(Folder);
            History?.OnRecordingStarted();
            UpdateSize(force: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Loc.F("Rec_StartError", Folder, ex.Message), "DJTube", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (IsRecording)
            _recorder.Pause();
        else if (IsPaused)
            _recorder.Resume();
    }

    [RelayCommand]
    private void Stop() => _recorder.Stop();

    [RelayCommand]
    private void DismissNotice()
    {
        _notice = null;
        OnPropertyChanged(nameof(NoticeText));
        OnPropertyChanged(nameof(HasNotice));
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            // Con una grabación, se abre la carpeta con el archivo seleccionado.
            string args = _recorder.CurrentFile is { } file && File.Exists(file) ? $"/select,\"{file}\"" : $"\"{Folder}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(ex.Message, "DJTube", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Guarda y cierra la grabación (al salir del programa).</summary>
    public void StopForExit() => _recorder.Stop();

    /// <summary>Se llama en cada fotograma.</summary>
    public void Refresh()
    {
        if (!IsActive)
        {
            if (Level > 0)
                Level = Math.Max(0, Level - MeterFallPerFrame);
            return;
        }

        string elapsed = TimeFormat.Clock(_recorder.Recorded);
        if (elapsed != _elapsedText)
        {
            _elapsedText = elapsed;
            OnPropertyChanged(nameof(ElapsedText));
            // El archivo se crea con el primer audio (y cambia al pasar a la parte siguiente).
            OnPropertyChanged(nameof(FileName));
            UpdateSize(force: false);
        }
        double target = EqMath.MeterFraction(_recorder.TakePeak());
        Level = target >= Level ? target : Math.Max(target, Level - MeterFallPerFrame);
    }

    private void OnRecorderChanged()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(FileName));
        _elapsedText = TimeFormat.Clock(_recorder.Recorded);
        OnPropertyChanged(nameof(ElapsedText));
        UpdateSize(force: true);

        if (_lastState != RecorderState.Stopped && State == RecorderState.Stopped && _recorder.Files.Count > 0)
            _ = FinishRecordingAsync(_recorder.Files);
        _lastState = State;
    }

    private void UpdateSize(bool force)
    {
        // El tamaño y el espacio libre cambian despacio: basta con mirarlos cada pocos segundos.
        long now = Environment.TickCount64;
        if (!force && now - _lastSizeUpdate < 3000)
            return;
        _lastSizeUpdate = now;

        long? free = null;
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(Folder));
            if (root is not null)
                free = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
        }

        IsLowSpace = free < LowSpaceBytes;
        long bytes = _recorder.Bytes;
        SizeText = (bytes > 0, free) switch
        {
            (true, { } f) => Loc.F("Rec_SizeFree", FormatBytes(bytes), FormatBytes(f)),
            (true, null) => FormatBytes(bytes),
            (false, { } f) => Loc.F("Rec_Free", FormatBytes(f)),
            _ => "",
        };
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };
}
