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
        Assert.Equal(["Indépendant", "Action", "Stratégie"], games[1].Genres); // Indie, Shooter → Action, Tactical → Stratégie
        Assert.EndsWith("_glx_vertical_cover.jpg?namespace=gamesdb", games[0].CoverUrl); // .webp → .jpg (servi par GOG)
        Assert.Equal("goggalaxy://openGameView/gog_1207658924", StoreLaunchers.GalaxyGameViewUri(games[0].Key));
        Assert.Throws<ArgumentException>(() => StoreLaunchers.GalaxyGameViewUri("uplay_0d2ae42d"));
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
}
