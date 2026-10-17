using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.Core.Recording;

namespace DJTube.App.ViewModels;

/// <summary>Ventana del historial: lo que ha sonado en la sesión, para copiarlo o guardarlo.</summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly SessionHistory _history;

    public HistoryViewModel(SessionHistory history)
    {
        _history = history;
        foreach (var entry in history.Entries)
            Entries.Add(entry);
        history.EntryAdded += OnEntryAdded;
    }

    public ObservableCollection<HistoryEntry> Entries { get; } = [];

    public bool IsEmpty => Entries.Count == 0;

    private void OnEntryAdded(object? sender, HistoryEntry entry) =>
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Entries.Add(entry);
            OnPropertyChanged(nameof(IsEmpty));
        });

    /// <summary>Deja de escuchar el historial (al cerrar la ventana).</summary>
    public void Detach() => _history.EntryAdded -= OnEntryAdded;

    [RelayCommand]
    private void Copy()
    {
        if (Entries.Count > 0)
            Clipboard.SetText(SessionHistory.ToText(Entries, useRecordingTime: false));
    }

    [RelayCommand]
    private void Save()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"DJTube {DateTime.Now:yyyy-MM-dd HH.mm}.txt",
            Filter = "Texto (*.txt)|*.txt",
        };
        if (dialog.ShowDialog() == true)
            File.WriteAllText(dialog.FileName, SessionHistory.ToText(Entries, useRecordingTime: false));
    }
}
