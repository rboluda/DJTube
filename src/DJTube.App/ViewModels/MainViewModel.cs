using CommunityToolkit.Mvvm.ComponentModel;
using DJTube.Audio.Bass;

namespace DJTube.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Libraries = BassLibraryInfo.GetVersions()
            .Select(l => $"{l.Library}  {l.Version}")
            .ToList();
    }

    /// <summary>Versiones de las DLL de BASS cargadas (comprobación de la fase 0).</summary>
    public IReadOnlyList<string> Libraries { get; }
}
