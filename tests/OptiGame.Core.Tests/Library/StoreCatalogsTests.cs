using OptiGame.Core.Library;
using OptiGame.Core.Profiles;

namespace OptiGame.Core.Tests.Library;

/// <summary>Données réelles de la machine de dev (2026-10-04) : extrait du catalogue Epic et valeurs lues dans la base de GOG Galaxy.</summary>
public sealed class StoreCatalogsTests
{
    private static byte[] Sample(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Library", "Samples", name));

    [Fact]
    public void Reads_owned_games_from_the_real_epic_catalog_cache()
    {
        // Extrait : Absolute Drift, Jurassic World Evolution, Twinmotion (asset Unreal), The Outer Worlds Peril on Gorgon (DLC).
        var games = StoreCatalogs.ParseEpicCatalog(Sample("epic-catcache-excerpt.bin"));

        Assert.Equal(["Jurassic World Evolution", "Absolute Drift"], games.Select(g => g.Name).OrderByDescending(n => n));
        var drift = Assert.Single(games, g => g.Name == "Absolute Drift");
        Assert.Equal((GameSource.Epic, "9d2f484bbec64aa8ad234b3199dcaf1c:9f5250193e914b849201a40d21b30939:19927295d6e3467887d4e830d8c85963"),
            (drift.Store, drift.Key));
        Assert.StartsWith("https://cdn1.epicgames.com/9d2f484bbec64aa8ad234b3199dcaf1c/item/EGS_AbsoluteDrift_FunselektorLabsInc_S2-1200x1600-", drift.CoverUrl);
        Assert.EndsWith(".jpg?h=528&w=396&resize=1", drift.CoverUrl);
    }

    private static GalaxyRow Row(string key, string title, string? genres = null, bool dlc = false, bool visible = true) =>
        new(key, dlc, visible, $$"""{"title":"{{title}}"}""",
            """{"verticalCover":"https://images.gog.com/c5e745857a368e028db804f4848c3dd0337b0fb8ff704401942a3e7364489c67_glx_vertical_cover.webp?namespace=gamesdb"}""",
            genres is null ? null : $$"""{"genres":[{{genres}}]}""");

    [Fact]
    public void Keeps_only_gog_games_from_galaxy()
    {
        var games = StoreCatalogs.FromGalaxy(
        [
            Row("gog_1207658924", "The Witcher: Enhanced Edition", "\"Role-playing (RPG)\""),
            Row("gog_1355225376", "Intravenous", "\"Indie\",\"Shooter\",\"Tactical\""),
            Row("gog_1207658924", "The Witcher: Enhanced Edition"), // doublon
            Row("gog_1", "Un DLC", dlc: true),
            Row("gog_2", "Jeu masqué", visible: false),
            // Intégrations de Galaxy (Epic périmée ; Ubisoft, EA et Xbox non prises en charge) → ignorées.
            Row("uplay_0d2ae42d-4c27-4cb7-af6c-2099062302bb", "Tom Clancy's Rainbow Six Siege", "\"Shooter\",\"Tactical\""),
            Row("origin_OFB-EAST:109544082", "The Sims™ 3", "\"Simulator\""),
            Row("epic_abc", "Jeu Epic vu par Galaxy"),
            Row("xboxone_157772240", "Jeu Xbox"),
        ]);

        Assert.Equal(["The Witcher: Enhanced Edition", "Intravenous"], games.Select(g => g.Name));
        Assert.All(games, g => Assert.Equal(GameSource.Gog, g.Store));
        Assert.Equal(["Indépendant", "Tir", "Stratégie", "Tactique"], games[1].Genres); // Indie, Shooter, Tactical (+ son parent)
        Assert.EndsWith("_glx_vertical_cover.jpg?namespace=gamesdb", games[0].CoverUrl); // .webp → .jpg (servi par GOG)
        Assert.Equal("goggalaxy://openGameView/gog_1207658924", StoreLaunchers.GalaxyGameViewUri(games[0].Key));
        Assert.Throws<ArgumentException>(() => StoreLaunchers.GalaxyGameViewUri("uplay_0d2ae42d"));
    }

    [Fact]
    public void Galaxy_themes_enrich_the_genres()
    {
        // originalMeta réelle de Styx: Shards of Darkness (gog_2090261953), relevée dans galaxy-2.0.db le 2026-10-06.
        var styx = new GalaxyRow("gog_2090261953", false, true, """{"title":"Styx: Shards of Darkness"}""", null,
            """{"criticsScore":70.7143,"developers":["Cyanide Studio"],"genres":["Role-playing (RPG)","Adventure"],"publishers":["Focus Entertainment"],"releaseDate":1489449600,"themes":["Action","Fantasy","Stealth"]}""");

        Assert.Equal(["RPG", "Aventure", "Action", "Fantasy", "Infiltration"], Assert.Single(StoreCatalogs.FromGalaxy([styx])).Genres);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", @"C:\x\absolutedrift.exe", GameSource.Epic)]
    [InlineData(@"C:\Program Files\GOG Galaxy\GalaxyClient.exe", @"C:\x\RoW.exe", GameSource.Gog)]
    [InlineData(null, @"A:\Jeux\StarCitizen\StarCitizen.exe", null)]
    public void Infers_the_store_of_an_installed_game(string? launcher, string exe, GameSource? store)
    {
        var profile = new GameProfile { Name = "x", ExePath = exe, LauncherPath = launcher };
        Assert.Equal(store, StoreCatalogs.StoreOf(profile, isSteamGame: false));
        Assert.Equal(GameSource.Steam, StoreCatalogs.StoreOf(profile, isSteamGame: true));
    }

    [Fact]
    public void Titles_are_compared_without_symbols() =>
        Assert.Equal(StoreCatalogs.NameKey("The Sims 3"), StoreCatalogs.NameKey("The Sims™ 3"));

    // Commandes exactes des raccourcis des lanceurs (StoreLaunchers), titres DIFFÉRENTS du catalogue : seul l'identifiant relie.
    private static InstalledGame Installed(GameSource store, string name, string arguments) =>
        new(name, store, @"D:\Jeux\X", [], LauncherPath: @"C:\Lanceur.exe", LaunchArguments: arguments);

    [Fact]
    public void Installed_epic_game_matches_its_catalog_line_by_identifier()
    {
        var installed = Installed(GameSource.Epic, "AbsoluteDrift",
            StoreLaunchers.Quoted(StoreLaunchers.EpicUri("4b5461ca8d1c488787b5200b420de066", "bd46d4ce259349e5bd8b3ded20274737", "Daisy", "launch")));
        var owned = new StoreOwnedGame(GameSource.Epic, "4b5461ca8d1c488787b5200b420de066:bd46d4ce259349e5bd8b3ded20274737:Daisy", "Absolute Drift", [], null);
        var other = new StoreOwnedGame(GameSource.Epic, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb:Other", "Autre jeu", [], null);

        Assert.True(StoreCatalogs.IsSameGame(installed, owned));
        Assert.False(StoreCatalogs.IsSameGame(installed, other));
    }

    [Fact]
    public void Installed_gog_game_matches_its_catalog_line_by_identifier()
    {
        var installed = Installed(GameSource.Gog, "Rites of War", StoreLaunchers.GogRunArguments("1207666553", @"D:\GOG Games\Rites of War"));

        Assert.True(StoreCatalogs.IsSameGame(installed, new StoreOwnedGame(GameSource.Gog, "gog_1207666553", "WARHAMMER 40,000: Rites of War", [], null)));
        Assert.False(StoreCatalogs.IsSameGame(installed, new StoreOwnedGame(GameSource.Gog, "gog_120766655", "Autre", [], null)));
    }

    [Fact]
    public void Same_title_matches_whatever_the_store()
    {
        var installed = Installed(GameSource.Gog, "The Sims™ 3", "/command=runGame /gameId=1 /path=\"D:\\X\"");
        Assert.True(StoreCatalogs.IsSameGame(installed, new StoreOwnedGame(GameSource.Epic, "a:b:c", "The Sims 3", [], null)));
    }
}
