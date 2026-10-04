using System.Text.Json;
using System.Text.RegularExpressions;

namespace OptiGame.Core.Updates;

/// <summary>Installeur d'une version publiée, avec l'empreinte SHA-256 que GitHub a calculée à son dépôt.</summary>
public sealed record UpdatePackage(
    Version Version,
    string Tag,
    Uri PageUrl,
    DateTimeOffset? PublishedAt,
    string InstallerName,
    Uri InstallerUrl,
    long InstallerSize,
    string Sha256);

public enum UpdateCheckStatus
{
    UpToDate,
    Available,

    /// <summary>Version plus récente publiée, mais inutilisable (installeur absent ou invérifiable) : ignorée.</summary>
    Unusable,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string Message, UpdatePackage? Package = null);

/// <summary>
/// Versions d'OptiGame publiées sur GitHub (API « releases/latest » : dernière version publiée, ni brouillon ni préversion).
/// Format relevé le 2026-10-04 sur une vraie réponse (échantillon dans les tests) : chaque fichier joint a « size »,
/// « state » et « digest » = « sha256:&lt;64 hexa&gt; », calculé par GitHub ; le téléchargement redirige vers
/// release-assets.githubusercontent.com.
/// </summary>
public static partial class AppReleases
{
    public const string Owner = "Antho69-jdr";
    public const string Repository = "OptiGame";

    public static readonly Uri LatestApi = new($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");

    /// <summary>Taille maximale acceptée pour un installeur (la 1.1.0 fait 46 Mo).</summary>
    public const long MaxInstallerBytes = 300L * 1024 * 1024;

    public static string InstallerName(Version version) => $"OptiGame-Setup-{version.ToString(3)}.exe";

    /// <summary>Nom d'un installeur d'OptiGame (« OptiGame-Setup-1.2.0.exe ») : seul fichier que la mise à jour accepte de lancer.</summary>
    public static bool IsInstallerName(string fileName) => InstallerFileName().IsMatch(fileName);

    /// <summary>
    /// Arguments de l'installeur pour une mise à jour lancée par OptiGame : sans aucune fenêtre (l'installeur ferme OptiGame
    /// proprement, comme pour une mise à jour à la main), puis OptiGame relancé (installer\OptiGame.iss, /RELAUNCH) dans la zone
    /// de notification, ou fenêtre ouverte si l'utilisateur l'avait demandée. Journal de l'installeur à côté de celui d'OptiGame.
    /// </summary>
    public static IReadOnlyList<string> SilentInstallArguments(bool showWindowAfter, string logFile) =>
        ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", showWindowAfter ? "/RELAUNCH=window" : "/RELAUNCH=minimized", $"/LOG={logFile}"];

    /// <summary>Page GitHub d'une version (nouveautés).</summary>
    public static Uri PageFor(Version version) => new($"https://github.com/{Owner}/{Repository}/releases/tag/v{version.ToString(3)}");

    /// <summary>« v1.2.0 » → 1.2.0 ; tout autre nom de tag → null.</summary>
    public static Version? ParseTag(string? tag) => tag is ['v', .. var rest] ? UpdatePolicy.Parse(rest) : null;

    /// <summary>Adresse de l'installeur annoncée par GitHub : exactement celle du fichier de cette version du dépôt d'OptiGame.</summary>
    public static bool IsOfficialAssetUrl(Uri url, string tag, string name) =>
        url.Scheme == Uri.UriSchemeHttps && url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && url.Query.Length == 0 &&
        url.AbsolutePath.Equals($"/{Owner}/{Repository}/releases/download/{tag}/{name}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Serveur atteint après les redirections : celui de GitHub qui sert les fichiers des versions (relevé le 2026-10-04), ou
    /// l'ancien (objects.githubusercontent.com). Rien d'autre.
    /// </summary>
    public static bool IsAllowedDownload(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps &&
        (url.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         url.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Compare la dernière version publiée (réponse JSON de <see cref="LatestApi"/> ; null = aucune version publiée) à la
    /// version qui tourne. Lève <see cref="JsonException"/> si la réponse n'est pas du JSON.
    /// </summary>
    public static UpdateCheckResult Evaluate(string? latestJson, Version current)
    {
        var running = current.ToString(3);
        if (latestJson is null) return new(UpdateCheckStatus.UpToDate, $"Aucune version d'OptiGame n'est encore publiée (vous avez la {running}).");

        using var document = JsonDocument.Parse(latestJson);
        var release = document.RootElement;
        if (release.ValueKind != JsonValueKind.Object) return new(UpdateCheckStatus.Unusable, "Réponse de GitHub inattendue : ignorée.");
        var tag = Text(release, "tag_name") ?? "";
        if (Flag(release, "draft") || Flag(release, "prerelease"))
        {
            return new(UpdateCheckStatus.Unusable, $"La version « {tag} » n'est pas publiée (brouillon ou préversion) : ignorée.");
        }
        if (ParseTag(tag) is not { } version)
        {
            return new(UpdateCheckStatus.Unusable, $"Version publiée « {tag} » non reconnue : ignorée.");
        }
        if (version <= current)
        {
            return new(UpdateCheckStatus.UpToDate, $"Vous avez la dernière version d'OptiGame ({running}).");
        }

        var latest = version.ToString(3);
        var name = InstallerName(version);
        var unusable = $"OptiGame {latest} est publié, mais sans installeur vérifiable ({name}) : il ne peut pas être installé pour l'instant.";
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return new(UpdateCheckStatus.Unusable, unusable);
        var asset = assets.EnumerateArray().FirstOrDefault(a => a.ValueKind == JsonValueKind.Object && Text(a, "name") == name);
        if (asset.ValueKind != JsonValueKind.Object ||
            Text(asset, "state") != "uploaded" ||
            !Uri.TryCreate(Text(asset, "browser_download_url"), UriKind.Absolute, out var url) || !IsOfficialAssetUrl(url, tag, name) ||
            !asset.TryGetProperty("size", out var sizeElement) || sizeElement.ValueKind != JsonValueKind.Number ||
            !sizeElement.TryGetInt64(out var size) || size is <= 0 or > MaxInstallerBytes ||
            Sha256Digest(Text(asset, "digest")) is not { } sha256)
        {
            return new(UpdateCheckStatus.Unusable, unusable);
        }

        var page = Uri.TryCreate(Text(release, "html_url"), UriKind.Absolute, out var html) &&
                   html.Scheme == Uri.UriSchemeHttps && html.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            ? html
            : PageFor(version);
        DateTimeOffset? published = release.TryGetProperty("published_at", out var date) && date.ValueKind == JsonValueKind.String &&
                                    date.TryGetDateTimeOffset(out var at) ? at : null;
        return new(UpdateCheckStatus.Available, $"OptiGame {latest} est disponible (vous avez la {running}).",
            new UpdatePackage(version, tag, page, published, name, url, size, sha256));
    }

    /// <summary>« sha256:&lt;64 hexa&gt; » (champ « digest » de GitHub) → empreinte en minuscules ; autre chose → null.</summary>
    public static string? Sha256Digest(string? digest) =>
        digest is not null && Digest().Match(digest) is { Success: true } match ? match.Groups[1].Value.ToLowerInvariant() : null;

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex("^sha256:([0-9a-fA-F]{64})$")]
    private static partial Regex Digest();

    [GeneratedRegex(@"^OptiGame-Setup-\d{1,4}\.\d{1,4}\.\d{1,4}\.exe$")]
    private static partial Regex InstallerFileName();
}
