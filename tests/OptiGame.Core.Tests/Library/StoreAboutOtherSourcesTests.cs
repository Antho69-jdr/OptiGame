using OptiGame.Core.Library;

namespace OptiGame.Core.Tests.Library;

/// <summary>Réponses réelles de GOG (produit, note) et d'IGDB (présentation d'un jeu), enregistrées le 2026-10-08.</summary>
public sealed class StoreAboutOtherSourcesTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Library", "Samples", name));

    [Fact]
    public void Gog_description_in_french_with_a_summary_and_browser_videos()
    {
        var (summary, details, videos) = GogStoreAbout.ParseProduct(Sample("gog-product-1207658924.json")); // The Witcher

        Assert.StartsWith("The Witcher est un jeu de rôles dans un monde sombre et fantastique", summary);
        Assert.True(summary.Length <= 321);
        Assert.Equal(summary.TrimEnd('…'), summary.TrimEnd('…')); // coupé à une fin de phrase ou marqué « … »
        Assert.All(details, b => Assert.DoesNotContain('<', b.Text));
        // Lecteur vidéo de GOG puis YouTube (adresse « embed » → page de la vidéo), 4 au plus.
        Assert.Equal(
            ["https://fast.wistia.net/embed/iframe/00177yg2c5", "https://fast.wistia.net/embed/iframe/fxai7rxzre",
             "https://www.youtube.com/watch?v=PZ_gex3U31U", "https://www.youtube.com/watch?v=iLCcXRoAY68"],
            videos.Select(v => v.Url));
    }

    [Fact]
    public void Gog_drops_images_and_keeps_text_even_when_the_publisher_wrote_english()
    {
        var (summary, details, _) = GogStoreAbout.ParseProduct(Sample("gog-product-2116968103.json")); // VirtuaVerse, image en tête
        Assert.StartsWith("Dans un futur proche, une Intelligence Artificielle", summary);
        Assert.All(details, b => Assert.DoesNotContain("items.gog.com", b.Text));

        var (english, _, videos) = GogStoreAbout.ParseProduct(Sample("gog-product-1680154175.json")); // Fiendish Freddy's
        Assert.StartsWith("All right, boys and girls.", english); // ligne de promotion trop courte : pas prise comme résumé
        Assert.Single(videos);
    }

    [Fact]
    public void Gog_rating_out_of_five_and_none_without_reviews()
    {
        Assert.Equal(new CommunityRating(4.4, 5, 3372), GogStoreAbout.ParseRating(Sample("gog-rating-1207658924.json")));
        Assert.Equal(new CommunityRating(2.8, 5, 28), GogStoreAbout.ParseRating(Sample("gog-rating-1680154175.json")));
        Assert.Null(GogStoreAbout.ParseRating("""{"value":0.0,"count":0}""")); // produit inconnu ou sans avis
        Assert.Equal("4,4", GogStoreAbout.Format(4.4));
    }

    [Fact]
    public void Igdb_gives_english_summary_player_votes_and_trailers_first()
    {
        var sc = IgdbAbout.Parse(Sample("igdb-about-1595.json"))!; // Star Citizen
        Assert.Equal(AboutSource.Igdb, sc.Source);
        Assert.True(sc.IsEnglish);
        Assert.StartsWith("Star Citizen is a sandbox open-world MMO", sc.Summary);
        Assert.Equal(100, sc.Community!.Scale);
        Assert.True(sc.Community.Count >= IgdbAbout.MinVotes);
        Assert.Null(sc.Press); // aucune critique relevée par IGDB
        Assert.Equal(SteamStoreAbout.MaxTrailers, sc.BrowserVideos.Count);
        Assert.Contains("Trailer", sc.BrowserVideos[0].Name, StringComparison.OrdinalIgnoreCase);
        Assert.All(sc.BrowserVideos, v => Assert.StartsWith("https://www.youtube.com/watch?v=", v.Url));
        Assert.Equal(sc.BrowserVideos.Count, sc.BrowserVideos.Select(v => v.Name).Distinct().Count()); // « Trailer », « Trailer 2 »…
    }

    [Fact]
    public void Igdb_hides_a_press_score_based_on_a_single_review()
    {
        var voidCrew = IgdbAbout.Parse(Sample("igdb-about-243017.json"))!; // 1 critique à 76
        Assert.Null(voidCrew.Press);

        var portal = IgdbAbout.Parse(Sample("igdb-about-72.json"))!; // Portal 2 : 9 critiques
        Assert.Equal(9, portal.Press!.Critics);
        Assert.InRange(portal.Press.Score, 90, 95);
    }

    [Fact]
    public void Igdb_unknown_game_gives_nothing()
    {
        Assert.Null(IgdbAbout.Parse(Sample("igdb-about-999999999.json"))); // « [] »
    }

    [Theory]
    [InlineData("https://www.youtube.com/embed/PZ_gex3U31U?wmode=opaque&rel=0", "https://www.youtube.com/watch?v=PZ_gex3U31U")]
    [InlineData("https://fast.wistia.net/embed/iframe/00177yg2c5", "https://fast.wistia.net/embed/iframe/00177yg2c5")]
    [InlineData("http://www.youtube.com/embed/PZ_gex3U31U", null)]
    [InlineData("https://evil.example/embed/PZ_gex3U31U", null)]
    [InlineData("https://www.youtube.com/embed/x", null)]
    public void Only_known_video_hosts_are_kept(string url, string? expected) => Assert.Equal(expected, WebVideo.Normalize(url));
}
