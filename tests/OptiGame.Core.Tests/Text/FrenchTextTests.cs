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
}
