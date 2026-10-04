using System.Text.Json;
using System.Text.RegularExpressions;

namespace OptiGame.Core.Library;

/// <summary>Jeu Epic installé (fichier <c>…\Epic\EpicGamesLauncher\Data\Manifests\*.item</c>).</summary>
public sealed record EpicInstall(string Name, string InstallLocation, string LaunchExecutable, string Namespace, string CatalogItemId, string AppName)
{
    public string ExePath => Path.Combine(InstallLocation, LaunchExecutable.Replace('/', '\\'));
}

/// <summary>
/// Lancement des jeux des autres magasins, sous la forme EXACTE des raccourcis que leurs lanceurs créent eux-mêmes (relevés sur
/// la machine de dev le 2026-10-04) :
/// <list type="bullet">
/// <item>Epic (Absolute Drift.url) : <c>com.epicgames.launcher://apps/&lt;namespace&gt;%3A&lt;item&gt;%3A&lt;app&gt;?action=launch&amp;silent=true</c> ;</item>
/// <item>Ubisoft (Steep.url) : <c>uplay://launch/3279/0</c> ;</item>
/// <item>GOG (raccourci du menu Démarrer) : <c>GalaxyClient.exe /command=runGame /gameId=1443606025 /path="…"</c> ;</item>
/// <item>EA (Les Sims 3.lnk) : l'exe du jeu directement.</item>
/// </list>
/// Les adresses passent au programme enregistré pour le protocole (HKCR\&lt;protocole&gt;\shell\open\command), comme un double-clic
/// sur le raccourci. Tout identifiant est vérifié avant d'entrer dans une commande.
/// </summary>
public static partial class StoreLaunchers
{
    public static EpicInstall? ParseEpicManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string? Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) return null;
        // Les extensions (DLC installés à part) désignent leur jeu dans MainGameAppName : on ne garde que les jeux.
        if (Text("MainGameAppName") is { Length: > 0 } main && main != Text("AppName")) return null;
        return Text("DisplayName") is { } name && Text("InstallLocation") is { } location && Text("LaunchExecutable") is { Length: > 0 } exe &&
               Text("CatalogNamespace") is { } ns && Text("CatalogItemId") is { } item && Text("AppName") is { } app
            ? new EpicInstall(name, location, exe, ns, item, app)
            : null;
    }

    public static string EpicUri(string catalogNamespace, string catalogItemId, string appName, string action) =>
        $"com.epicgames.launcher://apps/{Id(catalogNamespace)}%3A{Id(catalogItemId)}%3A{Id(appName)}?action={action}&silent=true";

    public static string UbisoftLaunchUri(string productId) =>
        IsDigits(productId) ? $"uplay://launch/{productId}/0" : throw new ArgumentException($"Identifiant Ubisoft invalide : « {productId} ».");

    public static string GogRunArguments(string gameId, string installPath) =>
        IsDigits(gameId) && !installPath.Contains('"')
            ? $"/command=runGame /gameId={gameId} /path=\"{installPath.TrimEnd('\\')}\""
            : throw new ArgumentException($"Jeu GOG invalide : « {gameId} ».");

    /// <summary>
    /// Page d'un jeu dans GOG Galaxy (releaseKey de sa base : « gog_… », « uplay_… », « origin_… »). Commande « openGameView » du
    /// protocole goggalaxy:// (celle qu'utilisent les intégrations connues de Galaxy) : NON vérifiée en vrai sur la machine de dev.
    /// </summary>
    public static string GalaxyGameViewUri(string releaseKey) =>
        releaseKey is { Length: > 0 and <= 120 } && releaseKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':')
            ? $"goggalaxy://openGameView/{releaseKey}"
            : throw new ArgumentException($"Jeu GOG Galaxy invalide : « {releaseKey} ».");

    /// <summary>Argument passé au programme du protocole : <c>"…exe" %1</c> (Epic) ou <c>"…exe" "%1"</c> (Ubisoft).</summary>
    public static string Quoted(string uri) => $"\"{uri}\"";

    /// <summary>« "C:\…\GalaxyClient.exe" /urlProtocol="%1" » → « C:\…\GalaxyClient.exe » (null si la commande est illisible).</summary>
    public static string? ExeOfCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var match = QuotedExe().Match(command);
        if (match.Success) return match.Groups[1].Value;
        var space = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return space > 0 ? command[..(space + 4)].Trim() : null;
    }

    private static bool IsDigits(string value) => value is { Length: > 0 and <= 20 } && value.All(char.IsAsciiDigit);

    /// <summary>Identifiants Epic : lettres, chiffres et tirets seulement (hexadécimal ou nom de code).</summary>
    private static string Id(string value) =>
        value is { Length: > 0 and <= 100 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')
            ? value
            : throw new ArgumentException($"Identifiant Epic invalide : « {value} ».");

    [GeneratedRegex("^\\s*\"([^\"]+\\.exe)\"", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedExe();
}
