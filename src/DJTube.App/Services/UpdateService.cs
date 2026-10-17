using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using DJTube.App.Localization;
using DJTube.Core.Updates;

namespace DJTube.App.Services;

/// <summary>
/// Busca actualizaciones en las releases de GitHub, descarga el instalador (verificando su
/// SHA-256) y lo lanza en modo silencioso; el instalador vuelve a abrir DJTube al terminar.
/// </summary>
public sealed class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    /// <summary>Instalado con el instalador (si no, es la versión portable y no se actualiza sola).</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public static SemanticVersion CurrentVersion =>
        SemanticVersion.TryParse(AppInfo.Version, out var v) ? v : new SemanticVersion(0, 0, 0);

    /// <summary>Consulta la última release publicada. null = no hay actualización.</summary>
    public async Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(UpdatePlanner.LatestReleaseApi, ct);
        // 404: el repositorio aún no tiene releases publicadas.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(ct);
        return UpdatePlanner.FindUpdate(CurrentVersion, UpdatePlanner.ParseRelease(json));
    }

    /// <summary>Descarga el instalador a %TEMP%\DJTube-Update y comprueba su huella.</summary>
    public async Task<string> DownloadAsync(ReleaseAsset installer, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (!UpdatePlanner.IsTrustedDownload(installer.DownloadUrl))
            throw new InvalidOperationException(installer.DownloadUrl.AbsoluteUri);

        string folder = Path.Combine(Path.GetTempPath(), "DJTube-Update");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Path.GetFileName(installer.Name));
        string partial = path + ".part";

        using (var response = await Http.GetAsync(installer.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? installer.Size;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(partial);
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0)
                    progress?.Report(Math.Min(1, done / (double)total));
            }
        }

        if (installer.Sha256 is { } expected)
        {
            string actual;
            await using (var stream = File.OpenRead(partial))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
            if (actual != expected)
            {
                File.Delete(partial);
                throw new InvalidDataException(Loc.T("Update_ChecksumError"));
            }
        }

        File.Move(partial, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// Lanza el instalador en modo silencioso (con su ventana de progreso). La aplicación debe
    /// cerrarse justo después; el instalador cierra lo que quede abierto y vuelve a abrir DJTube.
    /// </summary>
    public static void LaunchInstaller(string installerPath)
    {
        var language = Loc.Instance.Language == Loc.Spanish ? "spanish" : "english";
        Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            Arguments = $"/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /LANG={language}",
        });
    }

    public static void OpenPage(Uri page) => Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DJTube", AppInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
