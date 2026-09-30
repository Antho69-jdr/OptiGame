using OptiGame.Core.Artwork;

namespace OptiGame.Core.Tests.Artwork;

public sealed class IgdbTests
{
    // Structure conforme à la documentation IGDB v4 (champs étendus cover.image_id, artworks.image_id…).
    // À confirmer sur une vraie réponse : voir CLAUDE.md.
    private const string SampleResponse = """
        [
          {
            "id": 1942,
            "name": "The Witcher 3: Wild Hunt",
            "first_release_date": 1431993600,
            "cover": { "id": 89386, "image_id": "co1wyy" },
            "artworks": [ { "id": 1, "image_id": "ar5kz" }, { "id": 2, "image_id": "ar5l0" } ],
            "screenshots": [ { "id": 9742, "image_id": "mnljdjtrh44x4snmierh" } ]
          },
          {
            "id": 7,
            "name": "Jeu sans illustration",
            "screenshots": [ { "id": 3, "image_id": "sc123" } ]
          },
          { "id": 8, "name": "Jeu sans image" }
        ]
        """;

    [Fact]
    public void Parses_cover_hero_and_year()
    {
        var games = Igdb.ParseGames(SampleResponse);

        Assert.Equal(3, games.Count);
        Assert.Equal(new IgdbGame(1942, "The Witcher 3: Wild Hunt", 2015, "co1wyy", "ar5kz"), games[0]);
        Assert.Equal("sc123", games[1].HeroImageId); // pas d'illustration → capture d'écran
        Assert.Null(games[1].CoverImageId);
        Assert.Null(games[2].Year);
    }

    [Fact]
    public void Rejects_unexpected_response()
    {
        Assert.Throws<FormatException>(() => Igdb.ParseGames("""{ "message": "Authorization Failure" }"""));
    }

    [Fact]
    public void Search_query_escapes_quotes_and_requests_image_fields()
    {
        var query = Igdb.SearchQuery("Tom Clancy's \"Rainbow\" Six\\Siege", limit: 5);

        Assert.Equal(
            "search \"Tom Clancy's \\\"Rainbow\\\" Six\\\\Siege\"; " +
            "fields name,first_release_date,cover.image_id,artworks.image_id,screenshots.image_id; limit 5;",
            query);
    }

    [Fact]
    public void Image_url_follows_documented_template()
    {
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_cover_big_2x/co1wyy.jpg", Igdb.ImageUrl("co1wyy", Igdb.CoverSize));
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_1080p/ar5kz.jpg", Igdb.ImageUrl("ar5kz", Igdb.HeroSize));
    }

    [Fact]
    public void Best_match_prefers_exact_name_with_cover()
    {
        IReadOnlyList<IgdbGame> results =
        [
            new(1, "Overwatch 2", 2022, "c1", null),
            new(2, "Overwatch®", 2016, "c2", null),
            new(3, "Overwatch", 2016, null, null),
        ];

        Assert.Equal(2, Igdb.BestMatch("Overwatch", results)!.Id);   // ® ignoré ; le 3 n'a pas de jaquette
        Assert.Equal(1, Igdb.BestMatch("Star Citizen", results)!.Id); // pas d'égalité → premier avec jaquette
        Assert.Null(Igdb.BestMatch("x", []));
    }
}
