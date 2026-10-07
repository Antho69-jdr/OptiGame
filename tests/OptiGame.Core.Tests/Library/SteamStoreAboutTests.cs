using OptiGame.Core.Library;

namespace OptiGame.Core.Tests.Library;

/// <summary>Réponses réelles du magasin Steam (appdetails en français, appreviews), enregistrées le 2026-10-07.</summary>
public sealed class SteamStoreAboutTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Library", "Samples", name));

    [Fact]
    public void Reads_the_french_description_and_the_press_score()
    {
        var about = SteamStoreAbout.ParseDetails(Sample("steam-about-620.json"), "620")!;

        Assert.StartsWith("L'« initiative de tests perpétuels » a été étendue", about.Summary);
        Assert.Equal(new PressScore(95, "https://www.metacritic.com/game/pc/portal-2?ftag=MCD-06-10aaa1f"), about.Press);
        // « …sa musique.<br><br>La partie solo… » : un paragraphe par ligne, sans ligne vide.
        Assert.StartsWith("Portal 2 nous vient tout droit du jeu original et culte Portal", about.Details[0].Text);
        Assert.StartsWith("La partie solo de Portal 2", about.Details[1].Text);
        Assert.All(about.Details, b => Assert.DoesNotContain('<', b.Text));
    }

    [Fact]
    public void Keeps_section_titles_and_drops_images_and_videos()
    {
        var about = SteamStoreAbout.ParseDetails(Sample("steam-about-1808500.json"), "1808500")!;

        Assert.Null(about.Press); // pas de note de la presse pour ARC Raiders
        Assert.Equal(new GameAboutBlock("FOUILLEZ, SURVIVEZ, PROSPÉREZ", true), about.Details[0]);
        Assert.False(about.Details[1].IsHeading);
        Assert.StartsWith("Dans ARC Raiders, vous naviguez", about.Details[1].Text);
        Assert.Contains(about.Details, b => b.Text == "EXPLOREZ UN MONDE IMMERSIF" && b.IsHeading);
        Assert.All(about.Details, b => Assert.DoesNotContain("steamstatic", b.Text));
        Assert.All(about.Details, b => Assert.DoesNotContain("&", b.Text.Replace(" & ", ""))); // entités décodées
    }

    [Fact]
    public void Reads_trailers_highlighted_first_and_keeps_only_a_few()
    {
        var portal = SteamStoreAbout.ParseDetails(Sample("steam-about-620.json"), "620")!;
        Assert.Equal(SteamStoreAbout.MaxTrailers, portal.Trailers.Count); // 18 vidéos sur le magasin
        var first = portal.Trailers[0]; // la seule « highlight »
        Assert.Equal("Portal 2 E3 Demo (Excursion Funnels)", first.Name);
        Assert.Equal("https://video.akamai.steamstatic.com/store_trailers/620/3746/0f4bee66d490b637707ac4fca3e850f2cc2d3b2a/1750108721/hls_264_master.m3u8?t=1682715616", first.HlsUrl);
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/5787/movie.293x165.jpg?t=1682715616", first.ThumbnailUrl);

        var arc = SteamStoreAbout.ParseDetails(Sample("steam-about-1808500.json"), "1808500")!;
        Assert.All(arc.Trailers, t => Assert.EndsWith("hls_264_master.m3u8", new Uri(t.HlsUrl).AbsolutePath));
        Assert.Contains("movie_600x337.jpg", arc.Trailers[0].ThumbnailUrl);
    }

    [Fact]
    public void Ignores_trailers_outside_steam_servers()
    {
        var json = """{"1":{"success":true,"data":{"movies":[{"name":"x","thumbnail":"https://evil.example/t.jpg","hls_h264":"https://video.akamai.steamstatic.com/a.m3u8"},{"name":"y","thumbnail":"https://shared.akamai.steamstatic.com/t.jpg","hls_h264":"http://video.akamai.steamstatic.com/a.m3u8"}]}}}""";
        Assert.Empty(SteamStoreAbout.ParseDetails(json, "1")!.Trailers);
    }

    [Fact]
    public void Unknown_app_gives_nothing()
    {
        Assert.Null(SteamStoreAbout.ParseDetails("""{"123":{"success":false}}""", "123"));
    }

    [Fact]
    public void Reads_player_reviews_in_french()
    {
        var arc = SteamStoreAbout.ParseReviews(Sample("steam-reviews-1808500.json"))!;
        Assert.True(arc.IsRated);
        Assert.Equal((8, "très positives"), (arc.Score, arc.Label));
        Assert.True(arc.Total > 400_000);
        Assert.InRange(arc.Percent, 80, 84);

        var small = SteamStoreAbout.ParseReviews(Sample("steam-reviews-2104380.json"))!;
        Assert.Equal((7, "positives", 13, 14, 93), (small.Score, small.Label, small.Positive, small.Total, small.Percent));
    }

    [Fact]
    public void Too_few_reviews_are_not_rated_and_none_gives_nothing()
    {
        var few = SteamStoreAbout.ParseReviews(Sample("steam-reviews-2400020.json"))!;
        Assert.False(few.IsRated); // review_score 0 : « 3 évaluations »
        Assert.Equal((3, "3 évaluations"), (few.Total, few.Label));

        Assert.Null(SteamStoreAbout.ParseReviews(Sample("steam-reviews-480.json"))); // « aucune évaluation »
    }

    [Fact]
    public void Lists_become_bullets()
    {
        var blocks = SteamStoreAbout.HtmlToBlocks("<h2>Points forts</h2><ul class=\"bb_ul\"><li>Coop&nbsp;à 4</li><li><strong>Mods</strong> &amp; cartes</li></ul>");
        Assert.Equal(
            [new GameAboutBlock("Points forts", true), new GameAboutBlock("• Coop à 4", false), new GameAboutBlock("• Mods & cartes", false)],
            blocks);
    }
}
