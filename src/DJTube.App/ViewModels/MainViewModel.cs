using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Services;
using DJTube.Audio.Bass;
using DJTube.Core.Abstractions;
using DJTube.Core.Models;
using DJTube.Sources.YouTube;

namespace DJTube.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAudioEngine _engine;
    private readonly YouTubeTrackSource _youTube;
    private bool _initializing;

    [ObservableProperty]
    private AudioDevice? _selectedDevice;

    [ObservableProperty]
    private string _outputStatus = "Salida detenida";

    [ObservableProperty]
    private bool _hasOutputError;

    [ObservableProperty]
    private string _ytDlpStatus = "yt-dlp: comprobando…";

    [ObservableProperty]
    private bool _hasYtDlpError;

    public MainViewModel(
        IAudioEngine engine, ITrackSource localSource, YouTubeTrackSource youTube, IFileDialogService dialogs)
    {
        if (engine.Decks.Count < 2)
            throw new ArgumentException("El motor debe tener dos platos.", nameof(engine));

        _engine = engine;
        _youTube = youTube;
        DeckA = new DeckViewModel("A", engine.Decks[0], localSource, youTube, dialogs);
        DeckB = new DeckViewModel("B", engine.Decks[1], localSource, youTube, dialogs);
        Mixer = new MixerViewModel(engine.Decks[0], engine.Decks[1]);
        Libraries = string.Join(Environment.NewLine,
            BassLibraryInfo.GetVersions().Select(l => $"{l.Library} {l.Version}"));
    }

    public DeckViewModel DeckA { get; }

    public DeckViewModel DeckB { get; }

    public MixerViewModel Mixer { get; }

    public ObservableCollection<AudioDevice> Devices { get; } = [];

    /// <summary>Versiones de las DLL de BASS (para diagnóstico).</summary>
    public string Libraries { get; }

    public string CacheFolder => _youTube.CacheFolder;

    /// <summary>Enumera los dispositivos, arranca la salida en el predeterminado y comprueba yt-dlp.</summary>
    public void Initialize()
    {
        _initializing = true;
        try
        {
            Devices.Clear();
            foreach (var device in _engine.GetOutputDevices())
                Devices.Add(device);
            SelectedDevice = Devices.FirstOrDefault(d => d.IsDefault) ?? Devices.FirstOrDefault();
        }
        finally
        {
            _initializing = false;
        }
        StartOutput();
        _ = RefreshYtDlpStatusAsync();
    }

    /// <summary>Se llama en cada fotograma.</summary>
    public void Refresh()
    {
        DeckA.Refresh();
        DeckB.Refresh();
    }

    partial void OnSelectedDeviceChanged(AudioDevice? value)
    {
        if (!_initializing)
            StartOutput();
    }

    [RelayCommand]
    private async Task UpdateYtDlp()
    {
        string? exe = YtDlp.Find();
        if (exe is null)
        {
            await RefreshYtDlpStatusAsync();
            return;
        }

        YtDlpStatus = "yt-dlp: actualizando…";
        HasYtDlpError = false;
        try
        {
            string output = await YtDlp.UpdateAsync(exe);
            string last = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
            await RefreshYtDlpStatusAsync();
            if (last.Length > 0)
                YtDlpStatus += $" · {last}";
        }
        catch (Exception ex)
        {
            YtDlpStatus = $"yt-dlp: error al actualizar ({ex.Message})";
            HasYtDlpError = true;
        }
    }

    [RelayCommand]
    private void OpenCache()
    {
        System.IO.Directory.CreateDirectory(CacheFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{CacheFolder}\"") { UseShellExecute = true });
    }

    private async Task RefreshYtDlpStatusAsync()
    {
        try
        {
            string? exe = await Task.Run(YtDlp.Find);
            string? version = exe is null ? null : await YtDlp.GetVersionAsync(exe);
            YtDlpStatus = version is not null
                ? $"yt-dlp {version}"
                : "yt-dlp no encontrado: instálalo con «winget install yt-dlp.yt-dlp»";
            HasYtDlpError = version is null;
        }
        catch (Exception ex)
        {
            YtDlpStatus = $"yt-dlp: {ex.Message}";
            HasYtDlpError = true;
        }
    }

    private void StartOutput()
    {
        try
        {
            _engine.StartOutput(new AudioOutputSettings(SelectedDevice?.Id));
            var status = _engine.OutputStatus!;
            OutputStatus = $"WASAPI · {status.DeviceName} · {status.SampleRate} Hz · " +
                           $"{status.Channels} canales · buffer {status.LatencyMilliseconds:0.0} ms";
            HasOutputError = false;
        }
        catch (Exception ex)
        {
            OutputStatus = $"Error de salida: {ex.Message}";
            HasOutputError = true;
        }
    }
}
