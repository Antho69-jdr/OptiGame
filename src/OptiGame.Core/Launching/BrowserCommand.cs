using OptiGame.Core.Library;

namespace OptiGame.Core.Launching;

/// <summary>
/// Commande du navigateur par défaut pour une adresse web, d'après la commande enregistrée pour son type d'adresse
/// (HKCR\&lt;ProgId de UserChoice https&gt;\shell\open\command). Exemples réels : Firefox (machine de dev, 2026-10-06)
/// <c>"C:\Program Files\Mozilla Firefox\firefox.exe" -osint -url "%1"</c> ; Edge et Chrome <c>"…exe" --single-argument %1</c>.
/// Pourquoi pas explorer.exe : il ouvre « Documents » au lieu d'une adresse qui contient « ? » et « &amp; » (recherche du magasin
/// Epic, constaté le 2026-10-06).
/// </summary>
public static class BrowserCommand
{
    /// <summary>Programme et ligne de commande complète (programme en tête), ou null si la commande enregistrée est inutilisable.</summary>
    public static (string Exe, string CommandLine)? Build(string? registeredCommand, string url)
    {
        if (!IsSafeWebUrl(url) || StoreLaunchers.ExeOfCommand(registeredCommand) is not { } exe) return null;
        var command = registeredCommand!.Trim();
        // %1 est déjà entre guillemets dans la commande de Firefox ; seul (Edge, Chrome) l'adresse n'en a pas besoin : jamais d'espace.
        var line = command.Contains("%1", StringComparison.Ordinal) ? command.Replace("%1", url, StringComparison.Ordinal) : $"{command} \"{url}\"";
        line = line.Replace("%*", "", StringComparison.Ordinal).TrimEnd();
        return (exe, line);
    }

    /// <summary>Adresse https absolue, sans guillemet ni espace (elle ne peut pas sortir de son argument).</summary>
    public static bool IsSafeWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && !url.Any(c => c is '"' || char.IsWhiteSpace(c) || char.IsControl(c));
}
