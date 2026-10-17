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
            Title = "Cargar pista",
            Filter = $"Audio ({patterns})|{patterns}|Todos los archivos (*.*)|*.*",
            InitialDirectory = _lastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        };

        if (dialog.ShowDialog() != true)
            return null;

        _lastFolder = System.IO.Path.GetDirectoryName(dialog.FileName);
        return dialog.FileName;
    }
}
