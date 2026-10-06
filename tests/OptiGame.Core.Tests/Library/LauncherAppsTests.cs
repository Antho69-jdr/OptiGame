using OptiGame.Core.Library;

namespace OptiGame.Core.Tests.Library;

/// <summary>Chemins réels de la machine de dev (2026-10-06).</summary>
public sealed class LauncherAppsTests
{
    private const string Epic = @"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe";
    private const string Galaxy = @"C:\Program Files\GOG Galaxy\GalaxyClient.exe";

    [Fact]
    public void Epic_processes_are_the_launcher_folder_only()
    {
        var folder = LauncherApps.ProcessFolder(GameSource.Epic, Epic)!;

        Assert.Equal(@"C:\Program Files\Epic Games\Launcher", folder);
        Assert.True(LauncherApps.BelongsTo(folder, @"C:\Program Files\Epic Games\Launcher\Engine\Binaries\Win64\EpicWebHelper.exe"));
        // Epic Online Services sert aux JEUX : jamais arrêté avec le lanceur.
        Assert.False(LauncherApps.BelongsTo(folder, @"C:\Program Files (x86)\Epic Games\Epic Online Services\EpicOnlineServicesUserHelper.exe"));
        Assert.False(LauncherApps.BelongsTo(folder, @"C:\Program Files\Epic Games\LauncherOther\x.exe"));
        Assert.False(LauncherApps.BelongsTo(folder, null));
    }

    [Fact]
    public void Galaxy_processes_are_its_folder_and_steam_closes_itself()
    {
        var folder = LauncherApps.ProcessFolder(GameSource.Gog, Galaxy)!;

        Assert.True(LauncherApps.BelongsTo(folder, @"C:\Program Files\GOG Galaxy\QtWebEngineProcess.exe"));
        Assert.True(LauncherApps.BelongsTo(folder, @"C:\Program Files\GOG Galaxy\python\python.exe"));
        Assert.Null(LauncherApps.ProcessFolder(GameSource.Steam, @"C:\Program Files (x86)\Steam\steam.exe"));
        Assert.Null(LauncherApps.ProcessFolder(GameSource.Epic, @"D:\Ailleurs\EpicGamesLauncher.exe"));
        Assert.Equal("GOG Galaxy", LauncherApps.Name(GameSource.Gog));
    }
}
