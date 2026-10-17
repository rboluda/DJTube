using DJTube.App.Localization;
using DJTube.Sources.Local;
using Microsoft.Win32;

namespace DJTube.App.Services;

public interface IFileDialogService
{
    /// <summary>Pide al usuario un archivo de audio. Devuelve null si cancela.</summary>
    string? OpenAudioFile();
}

public sealed class FileDialogService : IFileDialogService
{
    private string? _lastFolder;

    public string? OpenAudioFile()
    {
        string patterns = string.Join(";", LocalFileTrackSource.SupportedExtensions.Select(e => "*" + e));
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Dialog_LoadTrack"),
            Filter = $"{Loc.F("Dialog_Audio", patterns)}|{patterns}|{Loc.T("Dialog_AllFiles")}|*.*",
            InitialDirectory = _lastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        };

        if (dialog.ShowDialog() != true)
            return null;

        _lastFolder = System.IO.Path.GetDirectoryName(dialog.FileName);
        return dialog.FileName;
    }
}
