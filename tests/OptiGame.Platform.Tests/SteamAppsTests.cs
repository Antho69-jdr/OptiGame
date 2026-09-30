using OptiGame.Platform.Library;

namespace OptiGame.Platform.Tests;

/// <summary>Index Steam sur une fausse bibliothèque (format réel des manifestes relevé sur la machine de dev).</summary>
public sealed class SteamAppsTests : IDisposable
{
    private readonly string _library = Path.Combine(Path.GetTempPath(), "OptiGame.Tests", Guid.NewGuid().ToString("N"), "SteamLibrary");

    public SteamAppsTests()
    {
        AddApp("578080", "PUBG: BATTLEGROUNDS", "PUBG");
        AddApp("228980", "Steamworks Common Redistributables", "Steamworks Shared");
        File.WriteAllText(Path.Combine(_library, "steamapps", "appmanifest_1.acf"), "pas un manifeste");
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_library)!, recursive: true); } catch (IOException) { }
    }

    private void AddApp(string appId, string name, string installDir)
    {
        var steamapps = Path.Combine(_library, "steamapps");
        Directory.CreateDirectory(Path.Combine(steamapps, "common", installDir));
        File.WriteAllText(Path.Combine(steamapps, $"appmanifest_{appId}.acf"), $$"""
            "AppState"
            {
            	"appid"		"{{appId}}"
            	"name"		"{{name}}"
            	"installdir"		"{{installDir}}"
            }
            """);
    }

    [Fact]
    public void Reads_games_and_skips_redistributables_and_broken_manifests()
    {
        var app = Assert.Single(GameLibraryScanner.SteamApps([_library]));

        Assert.Equal(("578080", "PUBG: BATTLEGROUNDS", "PUBG"), (app.AppId, app.Name, app.InstallDir));
    }

    [Fact]
    public void Finds_appid_of_an_exe_inside_the_install_folder_only()
    {
        var apps = GameLibraryScanner.SteamApps([_library]);
        var exe = Path.Combine(_library, "steamapps", "common", "PUBG", "TslGame", "Binaries", "Win64", "TslGame.exe");

        Assert.Equal("578080", GameLibraryScanner.FindSteamAppId(exe.ToUpperInvariant(), apps));
        Assert.Null(GameLibraryScanner.FindSteamAppId(Path.Combine(_library, "steamapps", "common", "PUBG2", "x.exe"), apps));
        Assert.Null(GameLibraryScanner.FindSteamAppId(@"A:\Jeux\StarCitizen.exe", apps));
    }
}
