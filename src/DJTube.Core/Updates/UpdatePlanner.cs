using System.Text.Json;

namespace DJTube.Core.Updates;

/// <summary>Archivo publicado en una release.</summary>
/// <param name="Sha256">Huella SHA-256 en hexadecimal que publica GitHub, si la hay.</param>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size, string? Sha256);

/// <summary>Release de GitHub (la parte que interesa a DJTube).</summary>
public sealed record ReleaseInfo(
    SemanticVersion Version, string TagName, Uri PageUrl, string Notes, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Actualización disponible: qué descargar y dónde ver las novedades.</summary>
/// <param name="Installer">Instalador (null si la release no lo trae).</param>
public sealed record AvailableUpdate(SemanticVersion Version, Uri PageUrl, string Notes, ReleaseAsset? Installer);

/// <summary>
/// Lee la respuesta de la API de releases de GitHub y decide si hay actualización.
/// Sin red: la descarga la hace la aplicación.
/// </summary>
public static class UpdatePlanner
{
    /// <summary>Solo se aceptan descargas de las releases de este repositorio.</summary>
    public const string Repository = "rboluda/DJTube";

    public static readonly Uri LatestReleaseApi = new($"https://api.github.com/repos/{Repository}/releases/latest");

    private static readonly string AllowedDownloadPrefix = $"https://github.com/{Repository}/releases/download/";

    /// <summary>Lee una release de la API de GitHub (GET /repos/{owner}/{repo}/releases/latest).</summary>
    /// <returns>null si la respuesta no es una release válida (borrador, sin versión, etc.).</returns>
    public static ReleaseInfo? ParseRelease(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || GetBool(root, "draft"))
                return null;

            string? tag = GetString(root, "tag_name");
            if (!SemanticVersion.TryParse(tag, out var version))
                return null;
            if (!Uri.TryCreate(GetString(root, "html_url"), UriKind.Absolute, out var page))
                return null;

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in list.EnumerateArray())
                {
                    string? name = GetString(asset, "name");
                    if (name is null || !Uri.TryCreate(GetString(asset, "browser_download_url"), UriKind.Absolute, out var url))
                        continue;
                    long size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out long n) ? n : 0;
                    assets.Add(new ReleaseAsset(name, url, size, ParseSha256(GetString(asset, "digest"))));
                }
            }

            return new ReleaseInfo(version, tag!, page, GetString(root, "body") ?? "", assets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Hay actualización si la release es más nueva que la versión instalada. El instalador
    /// se busca por nombre (DJTube-Setup-*.exe) y solo si se descarga de este repositorio.
    /// </summary>
    public static AvailableUpdate? FindUpdate(SemanticVersion current, ReleaseInfo? latest)
    {
        if (latest is null || !(latest.Version > current))
            return null;

        var installer = latest.Assets.FirstOrDefault(a =>
            a.Name.StartsWith("DJTube-Setup-", StringComparison.OrdinalIgnoreCase)
            && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && IsTrustedDownload(a.DownloadUrl));
        return new AvailableUpdate(latest.Version, latest.PageUrl, latest.Notes, installer);
    }

    public static bool IsTrustedDownload(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps && url.AbsoluteUri.StartsWith(AllowedDownloadPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>"sha256:ab12…" → "ab12…" (en minúsculas); cualquier otro formato → null.</summary>
    public static string? ParseSha256(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        string hex = digest[prefix.Length..].Trim().ToLowerInvariant();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
