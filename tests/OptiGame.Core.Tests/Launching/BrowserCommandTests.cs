using OptiGame.Core.Launching;

namespace OptiGame.Core.Tests.Launching;

public sealed class BrowserCommandTests
{
    private const string Search = "https://store.epicgames.com/fr/browse?q=Adios&sortBy=relevancy&sortDir=DESC";

    [Fact]
    public void Firefox_command_of_the_dev_machine()
    {
        // HKCR\FirefoxURL-308046B0AF4A39CB\shell\open\command, relevée le 2026-10-06.
        var command = BrowserCommand.Build("\"C:\\Program Files\\Mozilla Firefox\\firefox.exe\" -osint -url \"%1\"", Search);

        Assert.Equal((@"C:\Program Files\Mozilla Firefox\firefox.exe", $"\"C:\\Program Files\\Mozilla Firefox\\firefox.exe\" -osint -url \"{Search}\""), command);
    }

    [Fact]
    public void Edge_and_chrome_commands_take_the_address_alone()
    {
        var edge = BrowserCommand.Build("\"C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe\" --single-argument %1", Search);

        Assert.Equal($"\"C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe\" --single-argument {Search}", edge!.Value.CommandLine);
        Assert.EndsWith(" \"https://www.gog.com/game/x\"", BrowserCommand.Build("\"C:\\b\\browser.exe\"", "https://www.gog.com/game/x")!.Value.CommandLine);
    }

    [Theory]
    [InlineData("http://store.epicgames.com/")]
    [InlineData("https://x.com/\" --other")]
    [InlineData("https://x.com/a b")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    public void Only_safe_https_addresses_are_passed(string url) =>
        Assert.Null(BrowserCommand.Build("\"C:\\b\\browser.exe\" \"%1\"", url));

    [Fact]
    public void Unusable_registered_command_gives_nothing() => Assert.Null(BrowserCommand.Build(null, Search));
}
