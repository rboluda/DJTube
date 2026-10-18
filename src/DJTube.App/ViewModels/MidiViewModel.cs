using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Localization;
using DJTube.App.Services;
using DJTube.Core.Logic;
using DJTube.Core.Midi;

namespace DJTube.App.ViewModels;

/// <summary>Una fila de la ventana MIDI: acción de DJTube y el control asignado.</summary>
public partial class MidiActionRow : ObservableObject
{
    [ObservableProperty]
    private string _control = "";

    [ObservableProperty]
    private bool _isLearning;

    public MidiActionRow(MidiAction action, string label)
    {
        Action = action;
        Label = label;
    }

    public MidiAction Action { get; }

    public string Label { get; }
}

/// <summary>
/// Controladoras MIDI: recibe los mensajes, los traduce a acciones de DJTube (según lo asignado con
/// "aprender") y guarda las asignaciones en %APPDATA%\DJTube\midi.json.
/// </summary>
public partial class MidiViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DJTube", "midi.json");
    private readonly Dictionary<string, MidiAction> _actions = [];
    private MidiInput? _input;

    [ObservableProperty]
    private string _devicesText = "";

    [ObservableProperty]
    private string _lastMessage = "";

    public MidiViewModel(MainViewModel main)
    {
        _main = main;
        Mapping = MidiMapping.FromJson(File.Exists(_path) ? File.ReadAllText(_path) : null);
        Mapping.Learned += (_, e) =>
        {
            Save();
            RefreshRows();
        };
        BuildCatalog();
        RefreshRows();
    }

    public MidiMapping Mapping { get; }

    public ObservableCollection<MidiActionRow> Rows { get; } = [];

    /// <summary>Abre las controladoras conectadas (al arrancar y al pulsar "Buscar de nuevo").</summary>
    [RelayCommand]
    public void Connect()
    {
        try
        {
            _input ??= new MidiInput(OnMessage);
            _input.OpenAll();
            DevicesText = _input.Devices.Count == 0
                ? Loc.T("Midi_NoDevices")
                : Loc.F("Midi_Devices", string.Join(", ", _input.Devices));
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            DevicesText = ex.Message;
        }
    }

    [RelayCommand]
    private void Learn(MidiActionRow? row)
    {
        if (row is null)
            return;
        foreach (var r in Rows)
            r.IsLearning = r == row;
        Mapping.StartLearning(row.Action.Id);
    }

    [RelayCommand]
    private void ClearBinding(MidiActionRow? row)
    {
        if (row is null)
            return;
        Mapping.Unbind(row.Action.Id);
        Save();
        RefreshRows();
    }

    [RelayCommand]
    private void ClearAll()
    {
        Mapping.Clear();
        Save();
        RefreshRows();
    }

    public void StopLearning()
    {
        Mapping.CancelLearning();
        foreach (var r in Rows)
            r.IsLearning = false;
    }

    public void Dispose() => _input?.Dispose();

    /// <summary>Ejecuta una acción como si fuera un botón (atajos de teclado).</summary>
    public void Trigger(string actionId, bool press)
    {
        if (_actions.TryGetValue(actionId, out var action))
            Execute(action, new MidiMessage(new MidiSignal(MidiSignalKind.Note, 0, 0), press ? 127 : 0, 127));
    }

    /// <summary>Acciones de tipo botón (las que se pueden poner en una tecla), con su nombre.</summary>
    public IEnumerable<MidiActionRow> ButtonActions => Rows.Where(r => r.Action.Kind == MidiActionKind.Button);

    /// <summary>Un mensaje de la controladora (en el hilo de la interfaz).</summary>
    public void OnMessage(MidiMessage message)
    {
        LastMessage = $"{message.Signal} = {message.Value}";
        if (Mapping.Handle(message) is { } id && _actions.TryGetValue(id, out var action))
            Execute(action, message);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, Mapping.ToJson());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
        }
    }

    private void RefreshRows()
    {
        foreach (var row in Rows)
        {
            row.Control = Mapping.SignalFor(row.Action.Id)?.ToString() ?? "";
            row.IsLearning = Mapping.Learning == row.Action.Id;
        }
    }

    // ---------- Catálogo ----------

    private static readonly (string Name, MidiActionKind Kind)[] DeckActions =
    [
        ("PlayPause", MidiActionKind.Button), ("Cue", MidiActionKind.Button), ("Sync", MidiActionKind.Button),
        ("Load", MidiActionKind.Button), ("KeyLock", MidiActionKind.Button), ("Headphones", MidiActionKind.Button),
        ("Pitch", MidiActionKind.Absolute), ("Jog", MidiActionKind.Relative), ("Volume", MidiActionKind.Absolute),
        ("Gain", MidiActionKind.Absolute), ("High", MidiActionKind.Absolute), ("Mid", MidiActionKind.Absolute),
        ("Low", MidiActionKind.Absolute), ("Filter", MidiActionKind.Absolute),
        ("Loop", MidiActionKind.Button), ("LoopHalve", MidiActionKind.Button), ("LoopDouble", MidiActionKind.Button),
        ("Reloop", MidiActionKind.Button), ("Roll", MidiActionKind.Button),
        ("FxOn", MidiActionKind.Button), ("FxAmount", MidiActionKind.Absolute),
        ("HotCue1", MidiActionKind.Button), ("HotCue2", MidiActionKind.Button), ("HotCue3", MidiActionKind.Button),
        ("HotCue4", MidiActionKind.Button), ("HotCue5", MidiActionKind.Button), ("HotCue6", MidiActionKind.Button),
        ("HotCue7", MidiActionKind.Button), ("HotCue8", MidiActionKind.Button),
    ];

    private static readonly (string Id, MidiActionKind Kind)[] GlobalActions =
    [
        ("Mixer.Crossfader", MidiActionKind.Absolute), ("Mixer.Master", MidiActionKind.Absolute),
        ("Mixer.PhonesMix", MidiActionKind.Absolute), ("Mixer.PhonesVolume", MidiActionKind.Absolute),
        ("Library.Browse", MidiActionKind.Relative), ("App.Record", MidiActionKind.Button),
        ("App.Automix", MidiActionKind.Button),
    ];

    private void BuildCatalog()
    {
        foreach (string deck in new[] { "A", "B" })
        {
            foreach (var (name, kind) in DeckActions)
            {
                var action = new MidiAction($"Deck{deck}.{name}", kind);
                _actions[action.Id] = action;
                string label = name.StartsWith("HotCue", StringComparison.Ordinal)
                    ? Loc.F("MidiAct_HotCue", name["HotCue".Length..])
                    : Loc.T("MidiAct_" + name);
                Rows.Add(new MidiActionRow(action, $"{deck} · {label}"));
            }
        }
        foreach (var (id, kind) in GlobalActions)
        {
            var action = new MidiAction(id, kind);
            _actions[id] = action;
            Rows.Add(new MidiActionRow(action, Loc.T("MidiAct_" + id.Replace('.', '_'))));
        }
    }

    // ---------- Ejecutar ----------

    private void Execute(MidiAction action, MidiMessage m)
    {
        string[] parts = action.Id.Split('.');
        if (parts[0] is "DeckA" or "DeckB")
        {
            bool a = parts[0] == "DeckA";
            ExecuteDeck(a ? _main.DeckA : _main.DeckB, a ? _main.Mixer.EqA : _main.Mixer.EqB, a, parts[1], m);
            return;
        }

        double v = m.Normalized;
        switch (action.Id)
        {
            case "Mixer.Crossfader": _main.Mixer.Crossfader = v * 2 - 1; break;
            case "Mixer.Master": _main.Mixer.MasterVolume = v; break;
            case "Mixer.PhonesMix": _main.Mixer.CueMix = v; break;
            case "Mixer.PhonesVolume": _main.Mixer.HeadphoneVolume = v; break;
            case "Library.Browse": _main.Library.MoveSelection(m.RelativeDelta != 0 ? m.RelativeDelta : (m.IsPress ? 1 : 0)); break;
            case "App.Record" when m.IsPress:
                if (_main.Recording.IsActive)
                    _main.Recording.StopCommand.Execute(null);
                else
                    _main.Recording.RecordCommand.Execute(null);
                break;
            case "App.Automix" when m.IsPress: _main.Automix.ToggleCommand.Execute(null); break;
        }
    }

    private void ExecuteDeck(DeckViewModel deck, ChannelEqViewModel eq, bool isA, string name, MidiMessage m)
    {
        double v = m.Normalized;
        double bipolar = v * 2 - 1;
        switch (name)
        {
            case "PlayPause" when m.IsPress: deck.PlayPauseCommand.Execute(null); break;
            case "Cue": (m.IsPress ? deck.CueDownCommand : deck.CueUpCommand).Execute(null); break;
            case "Sync" when m.IsPress: deck.SyncCommand.Execute(null); break;
            case "Load" when m.IsPress: (isA ? _main.Library.LoadToACommand : _main.Library.LoadToBCommand).Execute(null); break;
            case "KeyLock" when m.IsPress: deck.KeyLock = !deck.KeyLock; break;
            case "Headphones" when m.IsPress:
                if (isA) _main.Mixer.CueA = !_main.Mixer.CueA;
                else _main.Mixer.CueB = !_main.Mixer.CueB;
                break;
            // Pitch: valor alto (fader arriba en casi todas las controladoras) = más lento, como en pantalla.
            case "Pitch": deck.PitchPercent = PitchMath.Clamp(-bipolar * deck.PitchRange, deck.PitchRange); break;
            case "Jog": deck.Jog(m.RelativeDelta); break;
            case "Volume":
                if (isA) _main.Mixer.VolumeA = v;
                else _main.Mixer.VolumeB = v;
                break;
            case "Gain": eq.Gain = bipolar; break;
            case "High": eq.High = bipolar; break;
            case "Mid": eq.Mid = bipolar; break;
            case "Low": eq.Low = bipolar; break;
            case "Filter": eq.Filter = bipolar; break;
            case "Loop" when m.IsPress: deck.ToggleLoopCommand.Execute(null); break;
            case "LoopHalve" when m.IsPress: deck.LoopHalveCommand.Execute(null); break;
            case "LoopDouble" when m.IsPress: deck.LoopDoubleCommand.Execute(null); break;
            case "Reloop" when m.IsPress: deck.ReloopCommand.Execute(null); break;
            case "Roll": (m.IsPress ? deck.RollDownCommand : deck.RollUpCommand).Execute(null); break;
            case "FxOn" when m.IsPress: deck.FxOn = !deck.FxOn; break;
            case "FxAmount": deck.FxAmount = v; break;
            default:
                if (name.StartsWith("HotCue", StringComparison.Ordinal) && m.IsPress
                    && int.TryParse(name["HotCue".Length..], out int n))
                    deck.HotCueCommand.Execute(n - 1);
                break;
        }
    }
}
