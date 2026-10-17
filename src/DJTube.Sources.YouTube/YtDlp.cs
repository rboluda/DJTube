using System.Diagnostics;
using System.Text;
using DJTube.Core.Localization;

namespace DJTube.Sources.YouTube;

/// <summary>
/// Localiza y ejecuta yt-dlp.exe como proceso externo.
/// </summary>
public static class YtDlp
{
    private const string ExeName = "yt-dlp.exe";

    /// <summary>Busca yt-dlp: el incluido con la app, junto a ella, en el PATH y donde lo instala winget.</summary>
    public static string? Find()
    {
        foreach (string candidate in Candidates())
        {
            try
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (IOException)
            {
            }
        }
        return null;
    }

    public static async Task<string?> GetVersionAsync(string exe, CancellationToken ct = default)
    {
        var lines = new List<string>();
        var result = await RunAsync(exe, ["--version"], line => lines.Add(line), ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? lines.FirstOrDefault(l => l.Length > 0) : null;
    }

    /// <summary>Actualiza yt-dlp ("-U") y devuelve su salida.</summary>
    public static async Task<string> UpdateAsync(string exe, CancellationToken ct = default)
    {
        var output = new StringBuilder();
        var result = await RunAsync(exe, ["-U"], line => output.AppendLine(line), ct).ConfigureAwait(false);
        if (result.ExitCode != 0 && output.Length == 0)
            output.Append(Messages.YtDlpExitCode(result.ExitCode));
        return output.ToString().Trim();
    }

    public readonly record struct RunResult(int ExitCode);

    /// <summary>
    /// Ejecuta yt-dlp sin ventana. <paramref name="onLine"/> recibe cada línea de stdout y stderr
    /// (desde hilos del sistema). Al cancelar se mata el proceso y sus hijos.
    /// </summary>
    public static async Task<RunResult> RunAsync(
        string exe, IEnumerable<string> arguments, Action<string> onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
            psi.ArgumentList.Add(argument);

        // yt-dlp necesita Deno del PATH. Primero el que viene con DJTube (carpeta tools); después el
        // PATH actual del sistema (si la app se abrió antes de instalar algo, el suyo está desactualizado).
        psi.Environment["PATH"] = string.Join(';', BundledToolsFolder, CurrentSystemPath());
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };

        if (!process.Start())
            throw new YouTubeSourceException(Messages.YtDlpRunFailed);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Ya había terminado.
            }
            throw;
        }
        return new RunResult(process.ExitCode);
    }

    /// <summary>Carpeta con yt-dlp y Deno que incluye el instalador de DJTube.</summary>
    public static string BundledToolsFolder => Path.Combine(AppContext.BaseDirectory, "tools");

    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(BundledToolsFolder, ExeName);
        yield return Path.Combine(AppContext.BaseDirectory, ExeName);

        foreach (string dir in CurrentSystemPath().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return Path.Combine(dir, ExeName);

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", ExeName);

        string packages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(packages))
        {
            foreach (string dir in Directory.EnumerateDirectories(packages, "yt-dlp.yt-dlp_*"))
                yield return Path.Combine(dir, ExeName);
        }
    }

    private static string CurrentSystemPath()
    {
        string[] parts =
        [
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "",
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "",
            Environment.GetEnvironmentVariable("PATH") ?? "",
        ];
        return string.Join(';', parts.Where(p => p.Length > 0));
    }
}
