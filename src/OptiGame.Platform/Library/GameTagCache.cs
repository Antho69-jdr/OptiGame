using OptiGame.Core;
using OptiGame.Core.Artwork;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;
using OptiGame.Core.State;

namespace OptiGame.Platform.Library;

/// <summary>Genres et types d'un jeu trouvés sur IGDB (taxonomy.json), par nom normalisé.</summary>
public sealed class GameTagCacheEntry
{
    public List<string> Genres { get; set; } = [];

    public List<GameKind> Kinds { get; set; } = [];

    /// <summary>Faux = IGDB ne connaît pas ce nom : nouvel essai après 30 jours.</summary>
    public bool Found { get; set; }

    public DateTimeOffset CheckedAt { get; set; }
}

public sealed class GameTagCacheDocument
{
    public int Version { get; set; } = 1;

    public Dictionary<string, GameTagCacheEntry> Games { get; set; } = [];
}

/// <summary>
/// Cache permanent des genres IGDB (« Genre » et « Type » de Mes jeux pour Epic, GOG, jeux ajoutés à la main, et en plus des
/// genres du magasin Steam). Clé = nom du jeu normalisé (sans ™ ®, ponctuation ni casse) : le même jeu sur deux magasins ne
/// coûte qu'une recherche. Fichier illisible = cache vide (il ne contient rien qui ne puisse être relu sur IGDB).
/// </summary>
public sealed class GameTagCache(AppPaths paths, FileLog log, TimeProvider time)
{
    private static readonly TimeSpan NotFoundMaxAge = TimeSpan.FromDays(30);

    private readonly Lock _lock = new();
    private readonly JsonStateStore<GameTagCacheDocument> _store = new(Path.Combine(paths.Root, "taxonomy.json"));
    private GameTagCacheDocument? _document;

    public static string Key(string name) => Igdb.Normalize(Igdb.CleanName(name));

    /// <summary>Genres et types connus pour ce nom, ou null (jamais cherché, ou introuvable sur IGDB).</summary>
    public GameTags? Find(string name)
    {
        lock (_lock)
        {
            return Document().Games.TryGetValue(Key(name), out var entry) && entry.Found
                ? new GameTags(entry.Genres, entry.Kinds.ToHashSet())
                : null;
        }
    }

    /// <summary>À chercher sur IGDB : jamais cherché, ou introuvable depuis plus de 30 jours.</summary>
    public bool NeedsLookup(string name)
    {
        lock (_lock)
        {
            return !Document().Games.TryGetValue(Key(name), out var entry)
                   || !entry.Found && time.GetUtcNow() - entry.CheckedAt > NotFoundMaxAge;
        }
    }

    /// <summary>Résultats d'une recherche (null = introuvable), enregistrés d'un coup.</summary>
    public void Store(IReadOnlyList<(string Name, GameTags? Tags)> results)
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            foreach (var (name, tags) in results)
            {
                Document().Games[Key(name)] = new GameTagCacheEntry
                {
                    Genres = tags?.Genres.ToList() ?? [],
                    Kinds = tags?.Kinds.Order().ToList() ?? [],
                    Found = tags is not null,
                    CheckedAt = now,
                };
            }
            _store.Save(Document());
        }
    }

    private GameTagCacheDocument Document()
    {
        if (_document is not null) return _document;
        try
        {
            _document = _store.Load() ?? new GameTagCacheDocument();
        }
        catch (StateFileCorruptException ex)
        {
            log.Warn($"Cache des genres illisible ({ex.Message}) : il sera reconstruit depuis IGDB.");
            _document = new GameTagCacheDocument();
        }
        return _document;
    }
}
