namespace OptiGame.Core.Library;

/// <summary>Genres (français) et types d'un jeu, quelle que soit leur source (Steam, GOG Galaxy, IGDB).</summary>
public sealed record GameTags(IReadOnlyList<string> Genres, IReadOnlySet<GameKind> Kinds)
{
    public static GameTags Empty { get; } = new([], new HashSet<GameKind>());

    public bool IsEmpty => Genres.Count == 0 && Kinds.Count == 0;

    /// <summary>Union de deux sources (ex. genres du magasin Steam + genres IGDB), sans doublon.</summary>
    public GameTags With(GameTags? other) => other is null || other.IsEmpty
        ? this
        : new(Genres.Concat(other.Genres).Distinct().ToList(), Kinds.Concat(other.Kinds).ToHashSet());
}

/// <summary>
/// Vocabulaire COMMUN des filtres « Genre » et « Type » de Mes jeux, en français, pour tous les magasins. Les genres et thèmes
/// d'IGDB (aussi ceux de GOG Galaxy, qui reprend IGDB : « Role-playing (RPG) », « Adventure »… relevés dans sa base le
/// 2026-10-06) et les modes de jeu IGDB y sont ramenés, comme les genres et catégories du magasin Steam (<see cref="SteamTaxonomy"/>).
/// Noms anglais d'IGDB : documentation api-docs.igdb.com (genres, themes, game_modes, player_perspectives).
/// </summary>
public static class GameTaxonomy
{
    private static readonly Dictionary<string, string[]> IgdbGenres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Adventure"] = ["Aventure"],
        ["Point-and-click"] = ["Aventure", "Point & click"],
        ["Visual Novel"] = ["Aventure", "Roman visuel"],
        ["Role-playing (RPG)"] = ["RPG"],
        ["Shooter"] = ["Tir"],
        ["Fighting"] = ["Combat"],
        ["Hack and slash/Beat 'em up"] = ["Beat'em all"],
        ["Platform"] = ["Plateforme"],
        ["Puzzle"] = ["Réflexion"],
        ["Racing"] = ["Course"],
        ["Sport"] = ["Sport"],
        ["Simulator"] = ["Simulation"],
        ["Strategy"] = ["Stratégie"],
        ["Real Time Strategy (RTS)"] = ["Stratégie", "Stratégie en temps réel"],
        ["Turn-based strategy (TBS)"] = ["Stratégie", "Tour par tour"],
        ["Tactical"] = ["Stratégie", "Tactique"],
        ["MOBA"] = ["MOBA"],
        ["Card & Board Game"] = ["Cartes et plateau"],
        ["Arcade"] = ["Arcade"],
        ["Music"] = ["Musique"],
        ["Quiz/Trivia"] = ["Quiz"],
        ["Pinball"] = ["Arcade"],
        ["Indie"] = ["Indépendant"],
    };

    private static readonly Dictionary<string, string[]> IgdbThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Action"] = ["Action"],
        ["Fantasy"] = ["Fantasy"],
        ["Science fiction"] = ["Science-fiction"],
        ["Horror"] = ["Horreur"],
        ["Thriller"] = ["Thriller"],
        ["Survival"] = ["Survie"],
        ["Historical"] = ["Historique"],
        ["Stealth"] = ["Infiltration"],
        ["Comedy"] = ["Humour"],
        ["Business"] = ["Gestion"],
        ["Sandbox"] = ["Bac à sable"],
        ["Open world"] = ["Monde ouvert"],
        ["Warfare"] = ["Guerre"],
        ["Party"] = ["Party game"],
        ["4X (explore, expand, exploit, and exterminate)"] = ["Stratégie", "4X"],
        ["Mystery"] = ["Enquête"],
        ["Educational"] = ["Éducatif"],
        ["Kids"] = ["Enfants"],
        // « Drama », « Non-fiction », « Romance », « Erotic » : pas des genres utiles pour filtrer une bibliothèque.
    };

    /// <summary>Genres français d'un nom de genre ou de thème IGDB (vide si inconnu ou écarté).</summary>
    public static IEnumerable<string> FromIgdbGenre(string name) => IgdbGenres.GetValueOrDefault(name) ?? [];

    public static IEnumerable<string> FromIgdbTheme(string name) => IgdbThemes.GetValueOrDefault(name) ?? [];

    /// <summary>Mode de jeu IGDB → types (et un genre pour MMO / Battle royale).</summary>
    public static (IEnumerable<GameKind> Kinds, IEnumerable<string> Genres) FromIgdbGameMode(string name) => name switch
    {
        "Single player" => ([GameKind.Solo], []),
        "Multiplayer" => ([GameKind.Multiplayer], []),
        "Co-operative" => ([GameKind.Multiplayer, GameKind.Coop], []),
        "Split screen" => ([GameKind.SplitScreen], []),
        "Massively Multiplayer Online (MMO)" => ([GameKind.Multiplayer], ["Massivement multijoueur"]),
        "Battle Royale" => ([GameKind.Multiplayer, GameKind.Pvp], ["Battle royale"]),
        _ => ([], []),
    };

    /// <summary>Point de vue IGDB : seule la réalité virtuelle donne un type.</summary>
    public static IEnumerable<GameKind> FromIgdbPerspective(string name) =>
        name.Equals("Virtual Reality", StringComparison.OrdinalIgnoreCase) ? [GameKind.Vr] : [];

    /// <summary>Genres, thèmes, modes et points de vue IGDB (noms anglais) → genres et types français.</summary>
    public static GameTags FromIgdb(IEnumerable<string> genres, IEnumerable<string> themes, IEnumerable<string> gameModes, IEnumerable<string> perspectives)
    {
        var modes = gameModes.Select(FromIgdbGameMode).ToList();
        var labels = genres.SelectMany(FromIgdbGenre).Concat(themes.SelectMany(FromIgdbTheme)).Concat(modes.SelectMany(m => m.Genres)).Distinct().ToList();
        var kinds = modes.SelectMany(m => m.Kinds).Concat(perspectives.SelectMany(FromIgdbPerspective)).ToHashSet();
        return new GameTags(labels, kinds);
    }
}
