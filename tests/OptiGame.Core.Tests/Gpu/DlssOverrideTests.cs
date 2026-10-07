using OptiGame.Core.Gpu;
using OptiGame.Core.Settings;

namespace OptiGame.Core.Tests.Gpu;

public sealed class DlssOverrideTests
{
    [Fact]
    public void Writes_the_official_override_and_latest_preset()
    {
        var id = Guid.NewGuid();
        const string exe = @"A:\SteamLibrary\steamapps\common\Overwatch\Overwatch.exe";

        var change = DlssOverride.Change(id, "Overwatch", exe, "Overwatch 2", "celui du jeu, rien d'imposé");

        Assert.Equal($"game.dlss-latest.{id:N}", change.Id);
        Assert.Contains("profil « Overwatch 2 »", change.What);
        Assert.Equal(
        [
            (KnownSettings.NvidiaSetting(exe, 0x10E41E01), 1u),        // NGX_DLSS_SR_OVERRIDE_ON
            (KnownSettings.NvidiaSetting(exe, 0x10E41DF3), 0x00FFFFFFu), // RENDER_PRESET_Latest
        ], change.Writes.Select(w => (w.Target, w.NewValue.AsDWord()!.Value)));
        Assert.Contains("OptiGame - Void Crew.exe", DlssOverride.Change(id, "Void Crew", @"C:\x\Void Crew.exe", null, "").What);
    }

    [Fact]
    public void Frame_cap_target_keeps_its_name_for_already_applied_changes() =>
        Assert.Equal("0x10835002", KnownSettings.NvidiaFrameRateLimit(@"C:\x\game.exe").Name);

    [Theory]
    [InlineData(null, null, "celui du jeu, rien d'imposé")]
    [InlineData(1u, 0x00FFFFFFu, "modèle le plus récent")]
    [InlineData(1u, 11u, "préréglage K")]
    [InlineData(0u, 0x00FFFFFFu, "celui du jeu, rien d'imposé")] // préréglage sans remplacement activé : sans effet
    public void Describes_the_current_override(uint? enabled, uint? preset, string text) =>
        Assert.Equal(text, DlssOverride.Describe(enabled, preset));

    [Theory]
    // Emplacements relevés le 2026-10-07 sur la machine de dev.
    [InlineData(@"A:\SteamLibrary\steamapps\common\Arc Raiders\PioneerGame\Binaries\Win64\PioneerGame.exe", @"A:\SteamLibrary\steamapps\common\Arc Raiders")]
    [InlineData(@"A:\SteamLibrary\steamapps\common\Overwatch\Overwatch.exe", @"A:\SteamLibrary\steamapps\common\Overwatch")]
    [InlineData(@"A:\Jeux\Roberts Space Industries\StarCitizen\LIVE\Bin64\StarCitizen.exe", @"A:\Jeux\Roberts Space Industries\StarCitizen\LIVE\Bin64")]
    [InlineData(@"D:\Jeux\MonJeu\MonProjet\Binaries\Win64\MonProjet-Win64-Shipping.exe", @"D:\Jeux\MonJeu")]
    public void Searches_the_game_folder(string exe, string root) => Assert.Equal(root, DlssOverride.SearchRoot(exe));
}
