using System.Globalization;
using System.Resources;

namespace DJTube.Core.Localization;

/// <summary>
/// Mensajes para el usuario que generan las librerías (errores de carga, descarga, audio…),
/// en el idioma de <see cref="CultureInfo.CurrentUICulture"/>. Textos en Resources/Messages*.resx.
/// </summary>
public static class Messages
{
    private static readonly ResourceManager Resources =
        new("DJTube.Core.Resources.Messages", typeof(Messages).Assembly);

    public static string TrackSourceMismatch(string source) => Format(nameof(TrackSourceMismatch), source);
    public static string UnsupportedFormat(string extension) => Format(nameof(UnsupportedFormat), extension);
    public static string FileNotFound => Get(nameof(FileNotFound));
    public static string StreamMustSeek => Get(nameof(StreamMustSeek));
    public static string InvalidVideoId(string id) => Format(nameof(InvalidVideoId), id);
    public static string YtDlpNotFound => Get(nameof(YtDlpNotFound));
    public static string YtDlpRunFailed => Get(nameof(YtDlpRunFailed));
    public static string YtDlpExitCode(int code) => Format(nameof(YtDlpExitCode), code);
    public static string DownloadFailed(string error) => Format(nameof(DownloadFailed), error);
    public static string DownloadFailedExitCode(int code) => Format(nameof(DownloadFailedExitCode), code);
    public static string BassInitFailed => Get(nameof(BassInitFailed));
    public static string OpenFailed(string fileName) => Format(nameof(OpenFailed), fileName);
    public static string TempoFailed => Get(nameof(TempoFailed));
    public static string MixerAddFailed => Get(nameof(MixerAddFailed));
    public static string MixerCreateFailed => Get(nameof(MixerCreateFailed));
    public static string NoDefaultOutput => Get(nameof(NoDefaultOutput));
    public static string InvalidOutputDevice(int device) => Format(nameof(InvalidOutputDevice), device);
    public static string OutputOpenFailed => Get(nameof(OutputOpenFailed));
    public static string OutputStartFailed => Get(nameof(OutputStartFailed));
    public static string AnalyzeFailed(string fileName) => Format(nameof(AnalyzeFailed), fileName);
    public static string OnlyFilesSupported => Get(nameof(OnlyFilesSupported));
    public static string LibraryNotFound => Get(nameof(LibraryNotFound));
    public static string DefaultDevice(string name) => Format(nameof(DefaultDevice), name);

    private static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    private static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), args);
}
