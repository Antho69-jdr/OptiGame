using OptiGame.Core.Library;

namespace OptiGame.Core.Tests.Library;

/// <summary>Données réelles de la machine de dev (2026-10-04) : manifeste Epic d'Absolute Drift et raccourcis créés par les lanceurs.</summary>
public sealed class StoreLaunchersTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Library", "Samples", name));

    [Fact]
    public void Reads_the_real_epic_manifest()
    {
        var game = StoreLaunchers.ParseEpicManifest(Sample("epic-manifest-absolutedrift.item"))!;
        Assert.Equal(("Absolute Drift", @"C:\Program Files\Epic Games\AbsoluteDrift\absolutedrift.exe"), (game.Name, game.ExePath));
        Assert.Equal(("9d2f484bbec64aa8ad234b3199dcaf1c", "9f5250193e914b849201a40d21b30939", "19927295d6e3467887d4e830d8c85963"),
            (game.Namespace, game.CatalogItemId, game.AppName));
    }

    [Fact]
    public void Incomplete_installs_and_add_ons_are_not_games()
    {
        Assert.Null(StoreLaunchers.ParseEpicManifest(Sample("epic-manifest-absolutedrift.item").Replace("\"bIsIncompleteInstall\": false", "\"bIsIncompleteInstall\": true")));
        Assert.Null(StoreLaunchers.ParseEpicManifest("""{"DisplayName":"Pack","InstallLocation":"C:\\x","LaunchExecutable":"x.exe","CatalogNamespace":"a","CatalogItemId":"b","AppName":"dlc","MainGameAppName":"jeu"}"""));
    }

    [Fact]
    public void Launch_commands_are_the_ones_of_the_launchers_shortcuts()
    {
        // Absolute Drift.url (bureau), créé par Epic.
        Assert.Equal("com.epicgames.launcher://apps/9d2f484bbec64aa8ad234b3199dcaf1c%3A9f5250193e914b849201a40d21b30939%3A19927295d6e3467887d4e830d8c85963?action=launch&silent=true",
            StoreLaunchers.EpicUri("9d2f484bbec64aa8ad234b3199dcaf1c", "9f5250193e914b849201a40d21b30939", "19927295d6e3467887d4e830d8c85963", "launch"));
        // Raccourci du menu Démarrer créé par GOG Galaxy.
        Assert.Equal(@"/command=runGame /gameId=1443606025 /path=""C:\Program Files\GOG Galaxy\Games\WARHAMMER 40K Rites of War""",
            StoreLaunchers.GogRunArguments("1443606025", @"C:\Program Files\GOG Galaxy\Games\WARHAMMER 40K Rites of War\"));

        Assert.Throws<ArgumentException>(() => StoreLaunchers.EpicUri("ns", "item?x=1", "app", "launch"));
        // Installation : même forme, silent=true compris (sans lui, le lanceur n'affiche que sa boutique).
        Assert.Equal("com.epicgames.launcher://apps/ns%3Aitem%3Aapp?action=install&silent=true", StoreLaunchers.EpicUri("ns", "item", "app", "install"));
    }

    [Theory]
    [InlineData(@"""C:\Program Files\GOG Galaxy\GalaxyClient.exe"" /urlProtocol=""%1""", @"C:\Program Files\GOG Galaxy\GalaxyClient.exe")]
    [InlineData(@"""C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"" %1", @"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe")]
    [InlineData(@"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop\EALauncher.exe ""%1""", @"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop\EALauncher.exe")]
    [InlineData("", null)]
    public void Finds_the_program_of_a_registered_protocol(string command, string? exe) // valeurs réelles de HKCR
    {
        Assert.Equal(exe, StoreLaunchers.ExeOfCommand(command));
    }

    [Fact]
    public void The_ea_installer_folder_never_holds_the_game()
    {
        var exes = new[]
        {
            new ExeFile(@"C:\Program Files\EA Games\The Sims 3\Game\Bin\TS3.exe", 14_887_256),
            new ExeFile(@"C:\Program Files\EA Games\The Sims 3\Game\Bin\Sims3Launcher.exe", 1_956_176),
            new ExeFile(@"C:\Program Files\EA Games\The Sims 3\__Installer\Touchup.exe", 989_720),
            new ExeFile(@"C:\Program Files\EA Games\The Sims 3\__Installer\Cleanup.exe", 988_696),
        };
        var ranked = ExeRanking.Rank("The Sims 3", "The Sims 3", exes);
        Assert.Equal("TS3.exe", Path.GetFileName(ranked[0].Path));
        Assert.DoesNotContain(ranked, e => e.Path.Contains("__Installer"));
    }
}
