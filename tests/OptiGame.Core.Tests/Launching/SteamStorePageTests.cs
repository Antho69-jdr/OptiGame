using OptiGame.Core.Launching;

namespace OptiGame.Core.Tests.Launching;

public sealed class SteamStorePageTests
{
    [Fact]
    public void Builds_client_and_web_addresses()
    {
        Assert.Equal("steam://store/578080", SteamStorePage.ClientUrl("578080"));
        Assert.Equal("https://store.steampowered.com/app/578080/", SteamStorePage.WebUrl("578080"));
    }

    [Fact]
    public void Client_command_line_matches_the_registered_steam_protocol()
    {
        Assert.Equal("\"C:\\Program Files (x86)\\Steam\\steam.exe\" -- \"steam://store/578080\"",
            SteamStorePage.ClientCommandLine(@"C:\Program Files (x86)\Steam\steam.exe", "578080"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("57808a")]
    [InlineData("1\" -shutdown \"")]
    public void Rejects_anything_but_a_numeric_appid(string appId)
    {
        Assert.Throws<ArgumentException>(() => SteamStorePage.ClientUrl(appId));
        Assert.Throws<ArgumentException>(() => SteamStorePage.ClientCommandLine("steam.exe", appId));
    }
}
