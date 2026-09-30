using OptiGame.Core.Launching;
using OptiGame.Core.Profiles;

namespace OptiGame.Core.Tests.Launching;

public sealed class LaunchPlannerTests
{
    private const string SteamExe = @"C:\Program Files (x86)\Steam\steam.exe";
    private const string PubgExe = @"A:\SteamLibrary\steamapps\common\PUBG\TslGame\Binaries\Win64\TslGame.exe";
    private const string StarCitizenExe = @"A:\Jeux\Roberts Space Industries\StarCitizen\LIVE\Bin64\StarCitizen.exe";

    private static GameProfile Profile(string exe, LaunchMode mode = LaunchMode.Automatic) =>
        new() { Name = "Jeu", ExePath = exe, LaunchMode = mode };

    private static string? SteamLookup(string exe) => exe == PubgExe ? "578080" : null;

    [Fact]
    public void Automatic_uses_steam_when_the_exe_is_in_a_steam_library()
    {
        var plan = LaunchPlanner.Plan(Profile(PubgExe), SteamLookup, SteamExe);

        Assert.Equal(SteamExe, plan.ExePath);
        Assert.Equal("-applaunch 578080", plan.Arguments);
        Assert.Equal($"\"{SteamExe}\" -applaunch 578080", plan.CommandLine);
    }

    [Fact]
    public void Automatic_falls_back_to_the_exe_outside_steam_or_without_steam()
    {
        Assert.Equal(StarCitizenExe, LaunchPlanner.Plan(Profile(StarCitizenExe), SteamLookup, SteamExe).ExePath);
        Assert.Equal(PubgExe, LaunchPlanner.Plan(Profile(PubgExe), SteamLookup, steamExe: null).ExePath);
    }

    [Fact]
    public void Stored_appid_wins_and_arguments_follow_it()
    {
        var profile = Profile(PubgExe);
        profile.SteamAppId = "123";
        profile.LaunchArguments = "  -high -dx12 ";

        var plan = LaunchPlanner.Plan(profile, SteamLookup, SteamExe);

        Assert.Equal("-applaunch 123 -high -dx12", plan.Arguments);
    }

    [Fact]
    public void Launcher_mode_starts_the_launcher_not_the_watched_exe()
    {
        var profile = Profile(StarCitizenExe, LaunchMode.Launcher);
        profile.LauncherPath = @"A:\Jeux\Roberts Space Industries\RSI Launcher\RSI Launcher.exe";

        var plan = LaunchPlanner.Plan(profile, SteamLookup, SteamExe);

        Assert.Equal(profile.LauncherPath, plan.ExePath);
        Assert.Equal($"\"{profile.LauncherPath}\"", plan.CommandLine);
        Assert.Contains("RSI Launcher.exe", plan.Description);
    }

    [Fact]
    public void Executable_mode_ignores_steam()
    {
        var plan = LaunchPlanner.Plan(Profile(PubgExe, LaunchMode.Executable), SteamLookup, SteamExe);

        Assert.Equal(PubgExe, plan.ExePath);
        Assert.Equal("", plan.Arguments);
    }

    [Fact]
    public void Missing_information_gives_a_clear_error()
    {
        Assert.Throws<LaunchException>(() => LaunchPlanner.Plan(Profile(StarCitizenExe, LaunchMode.Steam), SteamLookup, SteamExe));
        Assert.Throws<LaunchException>(() => LaunchPlanner.Plan(Profile(PubgExe, LaunchMode.Steam), SteamLookup, steamExe: null));
        Assert.Throws<LaunchException>(() => LaunchPlanner.Plan(Profile(StarCitizenExe, LaunchMode.Launcher), SteamLookup, SteamExe));
    }

    [Theory]
    [InlineData("578080", true)]
    [InlineData("12a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("12345678901", false)]
    public void Steam_appid_must_be_numeric(string? appId, bool valid)
    {
        Assert.Equal(valid, LaunchPlanner.IsValidSteamAppId(appId));
    }

    [Fact]
    public void Validator_rejects_bad_appid_and_launcher_without_exe()
    {
        var profile = Profile(StarCitizenExe, LaunchMode.Launcher);
        profile.SteamAppId = "abc";

        var errors = ProfileValidator.Validate(profile, []);

        Assert.Equal(2, errors.Count);
    }
}
