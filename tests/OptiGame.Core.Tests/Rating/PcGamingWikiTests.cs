using OptiGame.Core.Rating;

namespace OptiGame.Core.Tests.Rating;

/// <summary>Réponses réelles de l'API de PCGamingWiki, enregistrées le 2026-10-02.</summary>
public sealed class PcGamingWikiTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Rating", "Samples", $"pcgw-{name}.json"));

    private static SystemRequirements Page(string name) => PcGamingWiki.ParsePage(Sample(name), name)!;

    private static readonly PcSpecs Dev = new("NVIDIA GeForce RTX 3070", GpuPerformance.Identify("NVIDIA GeForce RTX 3070"), 32, 3440, 1440, 165);

    [Fact]
    public void Reads_the_Windows_requirements_of_a_game_sold_outside_Steam()
    {
        var sc = Page("star-citizen"); // RSI Launcher

        Assert.Equal(("PCGamingWiki", "https://www.pcgamingwiki.com/wiki/Star_Citizen"), (sc.Source, sc.SourceUrl));
        Assert.Equal("AMD Radeon HD7970 / Nvidia GeForce GTX 680", sc.Minimum!.GraphicsText);
        Assert.Equal(["HD 7970", "GTX 680"], sc.Minimum.Gpus.Select(g => g.Model));
        Assert.Equal(["R9 390", "GTX 980"], sc.Recommended!.Gpus.Select(g => g.Model));
        Assert.Equal((16, 16), (sc.Minimum.MemoryGb, sc.Recommended.MemoryGb)); // « 16+ GB DDR4 »
    }

    [Fact]
    public void Keeps_the_first_amount_of_memory_and_ignores_unknown_cards()
    {
        var minecraft = Page("minecraft"); // « 8 GB (discrete GPU) <br> 12 GB (integrated GPU) », Intel Arc A310 absente de la table
        Assert.Equal(8, minecraft.Minimum!.MemoryGb);
        Assert.Equal(["GTX 950", "RX 460"], minecraft.Minimum.Gpus.Select(g => g.Model));
        Assert.Equal(["RTX 2060", "RX 5600 XT"], minecraft.Recommended!.Gpus.Select(g => g.Model));

        var fortnite = Page("fortnite"); // bloc Windows, puis bloc OS X ignoré
        Assert.Equal(["GTX 960"], fortnite.Recommended!.Gpus.Select(g => g.Model));
        Assert.Equal((8, 16), (fortnite.Minimum!.MemoryGb, fortnite.Recommended.MemoryGb));
    }

    [Fact]
    public void A_game_whose_cards_are_all_unknown_gets_no_estimate()
    {
        var lol = Page("league-of-legends"); // GeForce 9600 GT, HD 6570, Intel HD 4600… : trop anciennes pour la table
        Assert.Empty(lol.Minimum!.Gpus);
        Assert.Equal(4, lol.Recommended!.MemoryGb);
        Assert.Null(GameRatings.EstimateFrom(Dev, lol));
    }

    [Fact]
    public void Estimate_and_rating_name_their_source()
    {
        var estimate = GameRatings.EstimateFrom(Dev, Page("star-citizen"))!;
        Assert.Equal((GraphicsPreset.High, 78), (estimate.Preset, estimate.Score)); // RTX 3070 en 3440×1440 ≈ 128 contre GTX 980 = 98

        var rating = GameRatings.Combine(estimate, null)!;
        Assert.Equal(("PCGamingWiki", "https://www.pcgamingwiki.com/wiki/Star_Citizen"), (rating.RequirementsSource, rating.RequirementsUrl));
    }

    [Theory]
    [InlineData("search-overwatch", "Overwatch", "Overwatch")]
    [InlineData("search-overwatch", "Overwatch 2", "Overwatch 2")]
    [InlineData("search-pubg", "PUBG: BATTLEGROUNDS", "PUBG: Battlegrounds")]
    [InlineData("search-overwatch", "Overwatch Classic", null)] // pas de titre identique : rien plutôt qu'un autre jeu
    public void Only_an_identical_title_is_accepted(string sample, string gameName, string? expected)
    {
        Assert.Equal(expected, PcGamingWiki.PickTitle(Sample(sample), gameName));
    }

    [Fact]
    public void A_missing_page_or_empty_search_gives_nothing()
    {
        Assert.Null(PcGamingWiki.ParsePage(Sample("missing"), "Ce jeu n'existe pas"));
        Assert.Null(PcGamingWiki.PickTitle("""["Jeu Inexistant Xyz",[],[],[]]""", "Jeu Inexistant Xyz"));
        Assert.Null(PcGamingWiki.ParseWikitext("{{Infobox game|cover=x.jpg}}"));
    }
}
