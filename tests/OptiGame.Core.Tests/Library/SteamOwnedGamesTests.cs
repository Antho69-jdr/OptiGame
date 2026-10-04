using OptiGame.Core.Launching;
using OptiGame.Core.Library;

namespace OptiGame.Core.Tests.Library;

/// <summary>
/// Extrait réel des caches du client Steam de la machine de dev (2026-10-04, DiagDump --steam-cache-sample) : Void Crew,
/// Amnesia: The Dark Descent, les redistribuables Steamworks (outil), Dota 2 et Spacewar (paquet 0), FOR HONOR (licence absente
/// de la bibliothèque).
/// </summary>
public sealed class SteamOwnedGamesTests
{
    private static byte[] Sample(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Library", "Samples", name));

    private static readonly IReadOnlyList<SteamAppInfo> Apps = SteamBinaryCache.ParseAppInfo(Sample("appinfo-excerpt.vdf"));
    private static readonly IReadOnlyList<SteamPackageInfo> Packages = SteamBinaryCache.ParsePackageInfo(Sample("packageinfo-excerpt.vdf"));

    [Fact]
    public void Reads_the_real_binary_caches()
    {
        var amnesia = Assert.Single(Apps, a => a.AppId == 57300);
        Assert.Equal(("Amnesia: The Dark Descent", "game"), (amnesia.Name, amnesia.Type));
        Assert.Equal([1, 25, 23], amnesia.Genres); // Action, Aventure, Indépendant
        Assert.Contains(2, amnesia.Categories); // Solo
        Assert.Equal("tool", Assert.Single(Apps, a => a.AppId == 228980).Type);
        Assert.Equal("Void Crew", Assert.Single(Apps, a => a.AppId == 1063420).Name);

        Assert.Contains(Packages, p => p.PackageId == 0 && p.AppIds.Contains(570u)); // Dota 2 : paquet 0, donné à tous
    }

    [Fact]
    public void Owned_games_skip_tools_free_package_0_and_licences_missing_from_the_library()
    {
        var libraryCache = new HashSet<uint> { 1063420, 57300, 228980, 570, 480 }; // FOR HONOR n'y est pas
        var owned = SteamOwnedGames.Find(Apps, Packages, libraryCache);

        Assert.Equal(["Amnesia: The Dark Descent", "Void Crew"], owned.Select(g => g.Name));
    }

    [Fact]
    public void Another_cache_version_is_refused_instead_of_misread()
    {
        var data = Sample("appinfo-excerpt.vdf");
        data[0] = 0x28; // version 28
        Assert.Throws<FormatException>(() => SteamBinaryCache.ParseAppInfo(data));
    }

    [Fact]
    public void Genres_and_kinds_have_their_french_store_names()
    {
        Assert.Equal(["Action", "Aventure", "Indépendant"], new[] { 1, 25, 23 }.Select(SteamTaxonomy.Genre));
        Assert.Null(SteamTaxonomy.Genre(59)); // « Publication Web » : pas un genre de jeu

        var kinds = SteamTaxonomy.Kinds([2, 38, 53]); // Solo, Coopération en ligne, VR prise en charge
        Assert.Equal(new HashSet<GameKind> { GameKind.Solo, GameKind.Multiplayer, GameKind.Coop, GameKind.Vr }, kinds);
    }

    [Fact]
    public void Install_goes_through_the_steam_client()
    {
        Assert.Equal(@"""C:\Steam\steam.exe"" -- ""steam://install/1063420""", SteamStorePage.InstallCommandLine(@"C:\Steam\steam.exe", "1063420"));
        Assert.Throws<ArgumentException>(() => SteamStorePage.InstallUrl("1063420 & calc"));
    }
}
