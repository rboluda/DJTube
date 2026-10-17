using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DJTube.App.Services;
using DJTube.Audio.Bass;
using DJTube.Core.Abstractions;
using DJTube.Core.Models;

namespace DJTube.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAudioEngine _engine;
    private bool _initializing;

    [ObservableProperty]
    private AudioDevice? _selectedDevice;

    [ObservableProperty]
    private string _outputStatus = "Salida detenida";

    [ObservableProperty]
    private bool _hasOutputError;

    public MainViewModel(IAudioEngine engine, ITrackSource localSource, IFileDialogService dialogs)
    {
        if (engine.Decks.Count < 2)
            throw new ArgumentException("El motor debe tener dos platos.", nameof(engine));

        _engine = engine;
        DeckA = new DeckViewModel("A", engine.Decks[0], localSource, dialogs);
        DeckB = new DeckViewModel("B", engine.Decks[1], localSource, dialogs);
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

    /// <summary>Enumera los dispositivos y arranca la salida en el predeterminado.</summary>
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
