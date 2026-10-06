using OptiGame.Core.Artwork;
using OptiGame.Core.Library;

namespace OptiGame.Core.Tests.Library;

public sealed class GameTaxonomyTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Artwork", "Samples", name));

    [Fact]
    public void Igdb_names_become_one_french_vocabulary()
    {
        var tags = GameTaxonomy.FromIgdb(
            genres: ["Shooter", "Real Time Strategy (RTS)", "Inconnu"],
            themes: ["Open world", "Horror", "Romance"],
            gameModes: ["Single player", "Co-operative", "Battle Royale"],
            perspectives: ["Virtual Reality", "First person"]);

        Assert.Equal(["Tir", "Stratégie", "Stratégie en temps réel", "Monde ouvert", "Horreur", "Battle royale"], tags.Genres);
        Assert.Equal(new HashSet<GameKind> { GameKind.Solo, GameKind.Multiplayer, GameKind.Coop, GameKind.Pvp, GameKind.Vr }, tags.Kinds);
    }

    [Fact]
    public void Sources_are_merged_without_duplicates()
    {
        var steam = new GameTags(["Action", "Course"], new HashSet<GameKind> { GameKind.Solo });
        var igdb = new GameTags(["Course", "Sport"], new HashSet<GameKind> { GameKind.Multiplayer });

        var merged = steam.With(igdb);

        Assert.Equal(["Action", "Course", "Sport"], merged.Genres);
        Assert.Equal(new HashSet<GameKind> { GameKind.Solo, GameKind.Multiplayer }, merged.Kinds);
        Assert.Same(steam, steam.With(null));
        Assert.True(GameTags.Empty.IsEmpty);
    }

    [Fact]
    public void Multiquery_asks_exact_names_without_trademark_signs()
    {
        var body = Igdb.TaxonomyMultiQuery(["The Sims™ 3", "Say \"Hi\""]);

        Assert.Contains("query games \"0\" { fields name,total_rating_count,genres.name,themes.name,game_modes.name,player_perspectives.name; where name ~ \"The Sims 3\"; limit 5; };", body);
        Assert.Contains("where name ~ \"Say \\\"Hi\\\"\";", body);
        Assert.Throws<ArgumentException>(() => Igdb.TaxonomyMultiQuery([]));
        Assert.Throws<ArgumentException>(() => Igdb.TaxonomyMultiQuery(Enumerable.Range(0, 11).Select(i => $"Jeu {i}").ToList()));
        Assert.StartsWith("search \"The Sims 3\";", Igdb.TaxonomySearchQuery("The Sims® 3"));
    }

    [Fact]
    public void Reads_a_real_multiquery_response()
    {
        // Vraie réponse d'IGDB du 2026-10-06 pour « Portal 2 », « Hades » (deux jeux de ce nom) et un nom inexistant.
        var tags = Igdb.ParseTaxonomyMultiQuery(Sample("igdb-taxonomy-multiquery.json"), ["Portal 2", "Hades", "Un jeu qui n existe pas zzz"]);

        Assert.Contains("Réflexion", tags[0]!.Genres);
        Assert.Contains(GameKind.Coop, tags[0]!.Kinds);
        Assert.Contains("RPG", tags[1]!.Genres); // le Hades le plus noté (Supergiant), pas son homonyme
        Assert.Null(tags[2]);
    }
}
