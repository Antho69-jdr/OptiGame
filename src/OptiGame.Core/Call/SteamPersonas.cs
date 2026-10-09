using OptiGame.Core.Library;

namespace OptiGame.Core.Call;

/// <summary>Personne connue du client Steam de ce PC (ami, membre d'un groupe, joueur croisé…), proposée pour une demande d'ami.</summary>
public sealed record SteamPersona(string SteamId, string Name);

/// <summary>Ami OptiGame accepté des deux côtés, gardé sur ce PC (settings.json) : jamais sur le serveur.</summary>
public sealed record CallContact(string SteamId, string Name);

/// <summary>
/// Noms connus du client Steam : bloc « friends » de userdata\&lt;compte&gt;\config\localconfig.vdf. Ce n'est PAS la liste d'amis
/// (319 personnes sur la machine de dev le 2026-10-09 : groupes, discussions, joueurs croisés) : seulement des noms à proposer pour une
/// demande d'ami, que l'autre doit accepter.
/// </summary>
public static class SteamPersonas
{
    public static IReadOnlyList<SteamPersona> Read(string localConfigVdf, uint ownAccountId)
    {
        var friends = Vdf.Parse(localConfigVdf)["UserLocalConfigStore"]?["friends"];
        if (friends is null) return [];
        return friends.Children
            .Where(c => c.Value.Value is null && uint.TryParse(c.Key, out var id) && id != ownAccountId && id != 0)
            .Select(c => new SteamPersona(SteamFriendsLink.FromAccountId(uint.Parse(c.Key)), (c.Value.GetString("name") ?? "").Trim()))
            .Where(p => p.Name.Length > 0)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Recherche dans les noms (casse et accents ignorés) ; 50 au plus.</summary>
    public static IReadOnlyList<SteamPersona> Search(IReadOnlyList<SteamPersona> personas, string text)
    {
        var query = Fold(text.Trim());
        return personas.Where(p => query.Length == 0 || Fold(p.Name).Contains(query, StringComparison.Ordinal)).Take(50).ToList();
    }

    private static string Fold(string text) =>
        new string(text.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToLowerInvariant();
}
