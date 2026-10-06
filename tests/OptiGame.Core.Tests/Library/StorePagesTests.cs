using OptiGame.Core.Library;
using OptiGame.Core.Profiles;

namespace OptiGame.Core.Tests.Library;

/// <summary>Valeurs réelles relevées le 2026-10-06 (catalogue Epic, base GOG Galaxy, service productmapping, API produit GOG).</summary>
public sealed class StorePagesTests
{
    private const string DriftKey = "9d2f484bbec64aa8ad234b3199dcaf1c:9f5250193e914b849201a40d21b30939:19927295d6e3467887d4e830d8c85963";

    [Fact]
    public void Owned_games_give_their_store_product()
    {
        Assert.Equal(new StoreProduct(GameSource.Epic, "9d2f484bbec64aa8ad234b3199dcaf1c", "Absolute Drift"),
            StorePages.FromOwned(GameSource.Epic, DriftKey, "Absolute Drift"));
        Assert.Equal(new StoreProduct(GameSource.Gog, "1075740587", "Vambrace: Cold Soul"),
            StorePages.FromOwned(GameSource.Gog, "gog_1075740587", "Vambrace: Cold Soul"));
        Assert.Null(StorePages.FromOwned(GameSource.Gog, "uplay_0d2ae42d", "x"));
        Assert.Null(StorePages.FromOwned(GameSource.Epic, "pas-une-cle", "x"));
    }

    [Fact]
    public void Installed_epic_and_gog_profiles_give_their_store_product()
    {
        var epic = new GameProfile
        {
            Name = "Absolute Drift", ExePath = @"C:\x\AbsoluteDrift.exe",
            LaunchArguments = StoreLaunchers.Quoted(StoreLaunchers.EpicUri("9d2f484bbec64aa8ad234b3199dcaf1c", "9f5250193e914b849201a40d21b30939", "19927295d6e3467887d4e830d8c85963", "launch")),
        };
        var gog = new GameProfile { Name = "Rites of War", ExePath = @"C:\x\RoW.exe", LaunchArguments = StoreLaunchers.GogRunArguments("1207658924", @"C:\GOG\Rites") };

        Assert.Equal(new StoreProduct(GameSource.Epic, "9d2f484bbec64aa8ad234b3199dcaf1c", "Absolute Drift"), StorePages.FromProfile(epic));
        Assert.Equal(new StoreProduct(GameSource.Gog, "1207658924", "Rites of War"), StorePages.FromProfile(gog));
        Assert.Null(StorePages.FromProfile(new GameProfile { Name = "Star Citizen", ExePath = @"A:\x.exe" }));
    }

    [Fact]
    public void Epic_mapping_gives_the_page_or_nothing()
    {
        const string mapping = """{"9d2f484bbec64aa8ad234b3199dcaf1c":"absolute-drift","abc":"../evil","def":""}""";

        Assert.Equal("absolute-drift", StorePages.EpicSlug(mapping, "9d2f484bbec64aa8ad234b3199dcaf1c"));
        Assert.Null(StorePages.EpicSlug(mapping, "abc")); // jamais un chemin
        Assert.Null(StorePages.EpicSlug(mapping, "def"));
        Assert.Null(StorePages.EpicSlug(mapping, "inconnu"));
        Assert.Equal("https://store.epicgames.com/fr/p/absolute-drift", StorePages.EpicPageUrl("absolute-drift"));
    }

    [Fact]
    public void Gog_product_card_must_stay_on_gog()
    {
        Assert.Equal("https://www.gog.com/game/vambrace_cold_soul",
            StorePages.GogProductCard("""{"id":1075740587,"title":"Vambrace: Cold Soul","slug":"vambrace_cold_soul","links":{"product_card":"https://www.gog.com/game/vambrace_cold_soul"}}"""));
        Assert.Null(StorePages.GogProductCard("""{"links":{"product_card":"https://evil.example/game/x"}}"""));
        Assert.Null(StorePages.GogProductCard("""{"error":"not_found"}"""));
        Assert.Equal("https://api.gog.com/products/1075740587", StorePages.GogProductApiUrl("1075740587"));
        Assert.Throws<ArgumentException>(() => StorePages.GogProductApiUrl("1/../x"));
    }

    [Fact]
    public void Launcher_addresses()
    {
        Assert.Equal("com.epicgames.launcher://store/browse?q=Caravan%20SandWitch", StorePages.EpicLauncherSearchUri("Caravan SandWitch"));
        Assert.Equal("goggalaxy://openStoreUrl/embed.gog.com/game/vambrace_cold_soul", StorePages.GalaxyStoreUri("https://www.gog.com/game/vambrace_cold_soul"));
        Assert.Equal("goggalaxy://openStoreUrl/embed.gog.com/game/vambrace_cold_soul", StorePages.GalaxyStoreUri("https://www.gog.com/fr/game/vambrace_cold_soul"));
        Assert.Null(StorePages.GalaxyStoreUri("https://www.gog.com/fr/games?query=Vambrace"));
        Assert.Null(StorePages.GalaxyStoreUri("https://evil.example/game/x"));
    }

    [Fact]
    public void Search_is_the_fallback()
    {
        Assert.Equal("https://store.epicgames.com/fr/browse?q=Caravan%20SandWitch&sortBy=relevancy&sortDir=DESC",
            StorePages.SearchUrl(new StoreProduct(GameSource.Epic, "ns", "Caravan SandWitch")));
        Assert.Equal("https://www.gog.com/fr/games?query=Rites%20of%20War", StorePages.SearchUrl(new StoreProduct(GameSource.Gog, "1", "Rites of War")));
    }
}
