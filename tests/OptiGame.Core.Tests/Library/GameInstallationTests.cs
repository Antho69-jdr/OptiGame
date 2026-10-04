using OptiGame.Core.Library;

namespace OptiGame.Core.Tests.Library;

/// <summary>Profils réels de la machine de dev (2026-10-04) : 4 jeux désinstallés toujours affichés dans « Mes jeux ».</summary>
public sealed class GameInstallationTests
{
    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        @"A:\SteamLibrary\steamapps\common\Overwatch\Overwatch.exe",
    };

    private static readonly HashSet<string> Drives = new(StringComparer.OrdinalIgnoreCase) { @"A:\", @"C:\" };

    private static InstallState Of(string exe) => GameInstallation.Of(exe, Files.Contains, Drives.Contains);

    [Fact]
    public void A_present_exe_is_installed() =>
        Assert.Equal(InstallState.Installed, Of(@"A:\SteamLibrary\steamapps\common\Overwatch\Overwatch.exe"));

    [Fact]
    public void A_missing_exe_on_a_present_drive_is_uninstalled()
    {
        Assert.Equal(InstallState.Uninstalled, Of(@"C:\Program Files\Epic Games\AbsoluteDrift\absolutedrift.exe"));
        Assert.Equal(InstallState.Uninstalled, Of(@"c:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Steep\steep.exe"));
    }

    [Fact]
    public void A_missing_drive_is_not_an_uninstall()
    {
        Assert.Equal(InstallState.DriveUnavailable, Of(@"E:\Jeux\Portal 2\portal2.exe")); // disque USB débranché
        Assert.Equal(InstallState.DriveUnavailable, Of(@"\\nas\jeux\Game\game.exe"));
    }

    [Fact]
    public void An_incomplete_path_counts_as_uninstalled()
    {
        Assert.Equal(InstallState.Uninstalled, Of(""));
        Assert.Equal(InstallState.Uninstalled, Of(@"Jeux\game.exe"));
    }

    [Theory]
    [InlineData(@"a:\Jeux\x.exe", @"A:\")]
    [InlineData("C:/Games/x.exe", @"C:\")]
    [InlineData(@"\\nas\jeux\x.exe", @"\\nas\jeux\")]
    [InlineData(@"\\nas", null)]
    [InlineData(@"\\?\C:\x.exe", null)]
    [InlineData(@"C:x.exe", null)]
    public void Finds_the_root(string path, string? root) => Assert.Equal(root, GameInstallation.RootOf(path));
}
