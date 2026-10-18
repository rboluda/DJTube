using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Localization;
using DJTube.App.Services;
using DJTube.Core.Logic;

namespace DJTube.App.ViewModels;

/// <summary>Una fila del editor de atajos: acción y su tecla.</summary>
public partial class ShortcutRow : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyText))]
    private string? _gesture;

    [ObservableProperty]
    private bool _isCapturing;

    public ShortcutRow(string actionId, string label, string? gesture)
    {
        ActionId = actionId;
        Label = label;
        _gesture = gesture;
    }

    public string ActionId { get; }

    public string Label { get; }

    public string KeyText => Gesture is { } g
        ? KeyboardShortcuts.Display(g, Loc.T("Key_Shift")).Replace("Space", Loc.T("Key_Space")).Replace("Enter", Loc.T("Key_Enter"))
        : "";
}

/// <summary>
/// Ventana de preferencias: los cambios se aplican al pulsar Aceptar (el tema se ve al momento y Cancelar
/// lo deshace).
/// </summary>
public partial class PreferencesViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly AppSettings _s;
    private readonly string _originalTheme;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private LocalizedOption<OutputMode> _selectedOutputMode;

    [ObservableProperty]
    private bool _checkForUpdates;

    [ObservableProperty]
    private LocalizedOption<string> _selectedTheme;

    [ObservableProperty]
    private LocalizedOption<double> _selectedBuffer;

    [ObservableProperty]
    private bool _limiter;

    [ObservableProperty]
    private bool _autoGain;

    [ObservableProperty]
    private LocalizedOption<double> _selectedTarget;

    [ObservableProperty]
    private LocalizedOption<double> _selectedPitchRange;

    [ObservableProperty]
    private bool _quantize;

    [ObservableProperty]
    private bool _lockPlayingDeck;

    [ObservableProperty]
    private LocalizedOption<double> _selectedFade;

    [ObservableProperty]
    private bool _automixMatchTempo;

    [ObservableProperty]
    private LocalizedOption<RecordingFormat> _selectedFormat;

    [ObservableProperty]
    private string _recordingsFolder;

    [ObservableProperty]
    private LocalizedOption<double> _selectedCacheLimit;

    public PreferencesViewModel(MainViewModel main)
    {
        _main = main;
        _s = main.Settings;
        _originalTheme = ThemeManager.Current;

        Themes = ThemeManager.Names.Select(n => new LocalizedOption<string>(n, v => Loc.T("Theme_" + v))).ToList();
        Buffers = new double[] { 0, 10, 20, 40, 80, 120 }
            .Select(ms => new LocalizedOption<double>(ms, v => v == 0 ? Loc.T("Pref_BufferAuto") : $"{v:0} ms")).ToList();
        Targets = new double[] { -8, -9, -10, -11, -12, -14, -16 }
            .Select(l => new LocalizedOption<double>(l, v => v == GainMath.DefaultTargetLufs ? Loc.F("Pref_TargetDefault", v) : $"{v:0} LUFS"))
            .ToList();
        PitchRanges = [new(PitchMath.DefaultRange, _ => "±8 %"), new(PitchMath.WideRange, _ => "±16 %")];
        Fades = new double[] { 4, 6, 8, 10, 15, 20, 30 }.Select(f => new LocalizedOption<double>(f, v => $"{v:0} s")).ToList();
        Formats =
        [
            new(RecordingFormat.Wav, _ => Loc.T("Fmt_Wav")),
            new(RecordingFormat.Mp3, _ => Loc.T("Fmt_Mp3")),
            new(RecordingFormat.M4a, _ => Loc.T("Fmt_M4a")),
        ];
        CacheLimits = new double[] { 0, 2, 5, 10, 20, 50 }
            .Select(g => new LocalizedOption<double>(g, v => v == 0 ? Loc.T("Pref_NoLimit") : $"{v:0} GB")).ToList();

        _selectedLanguage = Languages.First(l => l.Code == Loc.Instance.Language);
        _selectedOutputMode = main.OutputModes.First(m => m.Value == main.OutputMode);
        _checkForUpdates = main.CheckForUpdates;
        _recordingsFolder = main.Recording.Folder;
        _selectedTheme = Themes.FirstOrDefault(t => t.Value == ThemeManager.Current) ?? Themes[0];
        _selectedBuffer = Closest(Buffers, _s.BufferMs);
        _limiter = _s.Limiter;
        _autoGain = _s.AutoGain;
        _selectedTarget = Closest(Targets, _s.TargetLufs);
        _selectedPitchRange = Closest(PitchRanges, _s.DefaultPitchRange);
        _quantize = _s.Quantize;
        _lockPlayingDeck = _s.LockPlayingDeck;
        _selectedFade = Closest(Fades, _s.AutomixFadeSeconds);
        _automixMatchTempo = _s.AutomixMatchTempo;
        _selectedFormat = Formats.FirstOrDefault(f => f.Value == main.Recording.Format) ?? Formats[1];
        _selectedCacheLimit = Closest(CacheLimits, _s.CacheLimitGb);
        CacheSizeText = Loc.F("Pref_CacheSize", main.Library.CacheSizeBytes / (1024.0 * 1024 * 1024));

        foreach (var row in main.Midi.ButtonActions)
        {
            string? gesture = main.KeyBindings.FirstOrDefault(kv => kv.Value == row.Action.Id).Key;
            Shortcuts.Add(new ShortcutRow(row.Action.Id, row.Label, gesture));
        }
    }

    public IReadOnlyList<LanguageOption> Languages => Loc.Languages;

    public IReadOnlyList<LocalizedOption<OutputMode>> OutputModes => _main.OutputModes;

    public IReadOnlyList<LocalizedOption<string>> Themes { get; }

    public IReadOnlyList<LocalizedOption<double>> Buffers { get; }

    public IReadOnlyList<LocalizedOption<double>> Targets { get; }

    public IReadOnlyList<LocalizedOption<double>> PitchRanges { get; }

    public IReadOnlyList<LocalizedOption<double>> Fades { get; }

    public IReadOnlyList<LocalizedOption<RecordingFormat>> Formats { get; }

    public IReadOnlyList<LocalizedOption<double>> CacheLimits { get; }

    public ObservableCollection<ShortcutRow> Shortcuts { get; } = [];

    public string CacheSizeText { get; }

    public string CacheFolder => _main.CacheFolder;

    public IRelayCommand OpenCacheCommand => _main.OpenCacheCommand;

    /// <summary>El tema se ve al momento (Cancelar vuelve al anterior).</summary>
    partial void OnSelectedThemeChanged(LocalizedOption<string> value) => ThemeManager.Apply(value.Value);

    [RelayCommand]
    private void BrowseRecordingsFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("Pref_RecordingsFolder"),
            InitialDirectory = System.IO.Directory.Exists(RecordingsFolder) ? RecordingsFolder : null,
        };
        if (dialog.ShowDialog() == true)
            RecordingsFolder = dialog.FolderName;
    }

    // ---------- Atajos de teclado ----------

    [RelayCommand]
    private void CaptureShortcut(ShortcutRow? row)
    {
        foreach (var r in Shortcuts)
            r.IsCapturing = r == row;
    }

    [RelayCommand]
    private void ClearShortcut(ShortcutRow? row)
    {
        if (row is not null)
            row.Gesture = null;
    }

    [RelayCommand]
    private void ResetShortcuts()
    {
        foreach (var row in Shortcuts)
        {
            row.IsCapturing = false;
            row.Gesture = KeyboardShortcuts.Defaults.FirstOrDefault(kv => kv.Value == row.ActionId).Key;
        }
    }

    /// <summary>Tecla pulsada con una fila en espera: se asigna (y se quita de otra acción que la tuviera).</summary>
    public bool TryCaptureKey(Key key, ModifierKeys modifiers)
    {
        if (Shortcuts.FirstOrDefault(r => r.IsCapturing) is not { } row)
            return false;
        row.IsCapturing = false;
        if (key == Key.Escape || KeyboardShortcuts.Gesture(key, modifiers) is not { } gesture)
            return true;
        foreach (var other in Shortcuts.Where(r => r.Gesture == gesture))
            other.Gesture = null;
        row.Gesture = gesture;
        return true;
    }

    public void Cancel()
    {
        if (ThemeManager.Current != _originalTheme)
            ThemeManager.Apply(_originalTheme);
    }

    public void Apply()
    {
        if (!string.Equals(RecordingsFolder, _main.Recording.Folder, StringComparison.OrdinalIgnoreCase))
            _main.Recording.Folder = RecordingsFolder;
        if (SelectedLanguage.Code != Loc.Instance.Language)
            _main.SetLanguageCommand.Execute(SelectedLanguage.Code);
        if (SelectedOutputMode.Value != _main.OutputMode)
            _main.OutputMode = SelectedOutputMode.Value;
        _main.CheckForUpdates = CheckForUpdates;

        bool bufferChanged = _s.BufferMs != SelectedBuffer.Value;
        _s.Theme = SelectedTheme.Value;
        _s.BufferMs = SelectedBuffer.Value;
        _s.Limiter = Limiter;
        _s.AutoGain = AutoGain;
        _s.TargetLufs = SelectedTarget.Value;
        _s.DefaultPitchRange = SelectedPitchRange.Value;
        _s.Quantize = Quantize;
        _s.LockPlayingDeck = LockPlayingDeck;
        _s.AutomixFadeSeconds = SelectedFade.Value;
        _s.AutomixMatchTempo = AutomixMatchTempo;
        _s.RecordingFormat = SelectedFormat.Value.ToString();
        _s.CacheLimitGb = SelectedCacheLimit.Value;

        var bindings = Shortcuts.Where(r => r.Gesture is not null).ToDictionary(r => r.Gesture!, r => r.ActionId);
        bool isDefault = bindings.Count == KeyboardShortcuts.Defaults.Count
            && bindings.All(kv => KeyboardShortcuts.Defaults.TryGetValue(kv.Key, out var a) && a == kv.Value);
        _main.SetKeyBindings(isDefault ? null : bindings);

        _main.ApplyOptions();
        _main.SaveSettings();
        if (bufferChanged)
            _main.RestartOutput();
    }

    private static LocalizedOption<double> Closest(IReadOnlyList<LocalizedOption<double>> options, double value) =>
        options.OrderBy(o => Math.Abs(o.Value - value)).First();
}
