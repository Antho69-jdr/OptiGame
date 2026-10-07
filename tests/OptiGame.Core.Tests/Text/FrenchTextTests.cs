using OptiGame.Core.Text;

namespace OptiGame.Core.Tests.Text;

public class FrenchTextTests
{
    [Theory]
    [InlineData(0, "0\u00A0jeu")]
    [InlineData(1, "1\u00A0jeu")]
    [InlineData(2, "2\u00A0jeux")]
    [InlineData(631, "631\u00A0jeux")]
    public void Count_agrees_like_french(int count, string expected) =>
        Assert.Equal(expected, FrenchText.Count(count, "jeu", "jeux"));

    [Fact]
    public void Number_groups_thousands_with_narrow_no_break_spaces()
    {
        Assert.Equal("418 369", FrenchText.Number(418_369));
        Assert.Equal("14", FrenchText.Number(14));
    }

    [Fact]
    public void Agree_returns_the_word_only() =>
        Assert.Equal("appliquées", FrenchText.Agree(3, "appliquée", "appliquées"));

    [Fact]
    public void Typeset_uses_no_break_spaces_before_high_punctuation_and_inside_quotes()
    {
        var result = FrenchText.Typeset("Retirer « Portal 2 » ? Attention : 5 % ; fini !");
        Assert.Equal("Retirer «\u202FPortal 2\u202F»\u202F? Attention\u00A0: 5\u202F%\u202F; fini\u202F!", result);
    }

    [Theory]
    [InlineData(@"C:\Jeux\portal2.exe")]
    [InlineData("Dernière analyse à 14:32")]
    [InlineData("https://www.nvidia.com")]
    public void Typeset_leaves_paths_times_and_urls_alone(string text) =>
        Assert.Equal(text, FrenchText.Typeset(text));

    [Fact]
    public void Typeset_accepts_null_and_empty()
    {
        Assert.Equal("", FrenchText.Typeset(null));
        Assert.Equal("", FrenchText.Typeset(""));
    }

    [Fact]
    public void Dates_use_one_french_format()
    {
        Assert.Equal("22 sept. 2026", FrenchText.Date(new DateOnly(2026, 9, 22)));
        Assert.Equal("3 mai 2026", FrenchText.Date(new DateTime(2026, 5, 3, 23, 59, 0)));
        Assert.Equal("3 mars 2026 à 9:05", FrenchText.DateAndTime(new DateTime(2026, 3, 3, 9, 5, 0)));
    }

    [Theory]
    [InlineData(0, "aujourd'hui à 14:32")]
    [InlineData(1, "hier à 14:32")]
    [InlineData(2, "le 20 sept. 2026 à 14:32")]
    public void When_is_relative_for_today_and_yesterday(int daysAgo, string expected)
    {
        var now = new DateTime(2026, 9, 22, 8, 0, 0);
        Assert.Equal(expected, FrenchText.When(new DateTime(2026, 9, 22, 14, 32, 0).AddDays(-daysAgo), now));
    }
}
