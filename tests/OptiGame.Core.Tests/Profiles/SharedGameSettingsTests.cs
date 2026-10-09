using OptiGame.Core.Profiles;

namespace OptiGame.Core.Tests.Profiles;

/// <summary>Fichier « .optigame » : réglages de partie partagés, relus strictement (il vient d'ailleurs).</summary>
public sealed class SharedGameSettingsTests
{
    private static GameProfile Portal() => new()
    {
        Name = "Portal 2",
        ExePath = @"A:\SteamLibrary\steamapps\common\Portal 2\portal2.exe",
        Enabled = true,
        PowerSchemeId = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
        Priority = GamePriority.High,
        SteamAppId = "620",
        LaunchArguments = "-novid",
        ProcessesToClose = [new ProcessToClose { ExeName = "chrome.exe", Relaunch = true }, new ProcessToClose { ExeName = "Discord.exe" }],
    };

    [Fact]
    public void Round_trip_keeps_the_session_settings_and_nothing_of_the_pc()
    {
        var json = SharedGameSettings.From(Portal()).ToJson();
        Assert.DoesNotContain(@"A:\", json); // aucun chemin du PC
        Assert.DoesNotContain("620", json); // ni identifiant de lanceur
        Assert.DoesNotContain("-novid", json); // ni arguments de lancement

        var (settings, ignored) = SharedGameSettings.Parse(json);
        Assert.Empty(ignored);
        Assert.Equal(("Portal 2", "portal2.exe", true, GamePriority.High), (settings.Game, settings.Exe, settings.Optimize, settings.Priority));
        Assert.Equal(new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), settings.PowerPlan);
        Assert.Equal(["chrome.exe", "Discord.exe"], settings.ClosePrograms.Select(p => p.ExeName));
        Assert.True(settings.ClosePrograms[0].Relaunch);
    }

    [Fact]
    public void A_custom_power_plan_of_this_pc_is_not_exported()
    {
        var profile = Portal();
        profile.PowerSchemeId = Guid.NewGuid(); // plan personnalisé : n'existe que sur ce PC
        Assert.Null(SharedGameSettings.From(profile).PowerPlan);
    }

    [Fact]
    public void Dangerous_or_invalid_programs_are_dropped_and_listed()
    {
        var json = """
            { "format": "optigame-reglages-de-partie", "version": 1, "jeu": "X", "exe": "C:\\Windows\\x.exe", "optimiser": true,
              "priorite": "Realtime",
              "programmesAFermer": [ { "exe": "explorer.exe" }, { "exe": "..\\..\\evil.exe" }, { "exe": "chrome.exe" }, { "exe": "CHROME.exe" },
                                     { "exe": "csrss.exe" }, { "exe": "notes.txt" } ] }
            """;
        var (settings, ignored) = SharedGameSettings.Parse(json);

        Assert.Equal(["chrome.exe"], settings.ClosePrograms.Select(p => p.ExeName)); // doublon ignoré sans bruit
        Assert.Equal("", settings.Exe); // un chemin n'est pas un nom d'exe
        Assert.Equal(GamePriority.Normal, settings.Priority); // « temps réel » n'existe pas dans OptiGame
        Assert.Contains(ignored, i => i.Contains("explorer.exe") && i.Contains("protégé"));
        Assert.Contains(ignored, i => i.Contains("csrss.exe"));
        Assert.Contains(ignored, i => i.Contains("evil.exe"));
        Assert.Contains(ignored, i => i.Contains("notes.txt"));
        Assert.Contains(ignored, i => i.Contains("Realtime"));
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("""{ "format": "autre-chose", "version": 1 }""")]
    [InlineData("""{ "version": 1 }""")]
    public void Other_files_are_refused(string json) => Assert.Throws<FormatException>(() => SharedGameSettings.Parse(json));

    [Fact]
    public void A_newer_version_asks_for_an_update_and_huge_files_are_refused()
    {
        var newer = Assert.Throws<FormatException>(() => SharedGameSettings.Parse("""{ "format": "optigame-reglages-de-partie", "version": 9 }"""));
        Assert.Contains("mettez OptiGame à jour", newer.Message);
        Assert.Throws<FormatException>(() => SharedGameSettings.Parse(new string(' ', SharedGameSettings.MaxBytes + 1)));
    }

    [Fact]
    public void Suggested_file_name_is_safe()
    {
        Assert.Equal("Portal 2 - réglages OptiGame.optigame", SharedGameSettings.SuggestedFileName("Portal 2"));
        Assert.DoesNotContain(':', SharedGameSettings.SuggestedFileName("PUBG: BATTLEGROUNDS"));
    }
}
