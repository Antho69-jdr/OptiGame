namespace OptiGame.Core.Library;

/// <summary>Jeu de la bibliothèque Steam (installé ou non) : genres et catégories du magasin.</summary>
public sealed record OwnedSteamGame(uint AppId, string Name, IReadOnlyList<int> Genres, IReadOnlyList<int> Categories);

/// <summary>Type de jeu, d'après les catégories du magasin Steam.</summary>
public enum GameKind
{
    Solo,
    Multiplayer,
    Coop,
    Pvp,
    SplitScreen,
    Vr,
}

/// <summary>
/// Jeux possédés d'après les caches du client Steam (<see cref="SteamBinaryCache"/>). Règles vérifiées sur la machine de dev le
/// 2026-10-04 (283 jeux sous licence, 277 dans la bibliothèque) :
/// <list type="bullet">
/// <item>type « game » seulement (pas les DLC, outils, démos, bêtas) ;</item>
/// <item>licence d'un paquet autre que le paquet 0, qui donne à TOUS les comptes Dota 2, Team Fortress 2 et Spacewar (application
/// de test de Valve) ;</item>
/// <item>présent dans <c>appcache\librarycache</c> (ce que la bibliothèque du client affiche) : 5 licences sans ce cache
/// (ex. week-ends gratuits, partages expirés) sont écartées plutôt que proposées à tort.</item>
/// </list>
/// </summary>
public static class SteamOwnedGames
{
    public static IReadOnlyList<OwnedSteamGame> Find(IReadOnlyList<SteamAppInfo> apps, IReadOnlyList<SteamPackageInfo> packages,
        IReadOnlySet<uint> inLibraryCache)
    {
        var licensed = packages.Where(p => p.PackageId != 0).SelectMany(p => p.AppIds).ToHashSet();
        return apps
            .Where(a => a.Type == "game" && licensed.Contains(a.AppId) && inLibraryCache.Contains(a.AppId))
            .Select(a => new OwnedSteamGame(a.AppId, a.Name, a.Genres, a.Categories))
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

/// <summary>
/// Noms français des genres et catégories du magasin Steam, relevés le 2026-10-04 sur les réponses de
/// store.steampowered.com/api/appdetails (l=french) pour un jeu de chaque genre de la bibliothèque de la machine de dev.
/// </summary>
public static class SteamTaxonomy
{
    private static readonly Dictionary<int, string> GenreNames = new()
    {
        [1] = "Action", [2] = "Stratégie", [3] = "RPG", [4] = "Occasionnel", [9] = "Course automobile", [18] = "Sport",
        [23] = "Indépendant", [25] = "Aventure", [28] = "Simulation", [29] = "Massivement multijoueur", [37] = "Free-to-play",
        [70] = "Accès anticipé",
    };

    /// <summary>Genre connu ; null pour un identifiant non vérifié (ex. 59 « Publication Web » : pas un genre de jeu).</summary>
    public static string? Genre(int id) => GenreNames.GetValueOrDefault(id);

    // Catégories (même source) : 2 Solo ; 1 Multijoueur ; 9 Coopération, 38 Coopération en ligne, 48 Coop en LAN ;
    // 49 PvP, 36 JcJ en ligne, 47 JcJ en LAN, 37 JcJ en écran partagé ; 24 Écran partagé ; 31 Prise en charge VR,
    // 53 VR prise en charge, 54 VR uniquement.
    private static readonly (GameKind Kind, int[] Categories)[] KindCategories =
    [
        (GameKind.Solo, [2]),
        (GameKind.Multiplayer, [1, 49, 36, 38, 9, 27, 20]),
        (GameKind.Coop, [9, 38, 48]),
        (GameKind.Pvp, [49, 36, 47, 37]),
        (GameKind.SplitScreen, [24, 37]),
        (GameKind.Vr, [31, 53, 54]),
    ];

    public static IReadOnlySet<GameKind> Kinds(IReadOnlyList<int> categories) =>
        KindCategories.Where(k => k.Categories.Any(categories.Contains)).Select(k => k.Kind).ToHashSet();

    public static string Label(GameKind kind) => kind switch
    {
        GameKind.Solo => "Solo",
        GameKind.Multiplayer => "Multijoueur",
        GameKind.Coop => "Coopération",
        GameKind.Pvp => "JcJ (compétitif)",
        GameKind.SplitScreen => "Écran partagé",
        _ => "VR",
    };
}
