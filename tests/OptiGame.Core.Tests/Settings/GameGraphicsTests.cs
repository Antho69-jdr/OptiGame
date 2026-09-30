using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Tests.Settings;

public sealed class GameGraphicsTests
{
    // Valeurs réelles de UserGpuPreferences sur la machine de dev (2026-09-30).
    private const string Pubg = @"A:\SteamLibrary\steamapps\common\PUBG\TslGame\Binaries\Win64\TslGame.exe";
    private const string PubgDefault = "AppStatus=1;AutoHDREnable=2097;";
    private const string PubgAfterUserChange = "SwapEffectUpgradeEnable=1;AutoHDREnable=6193;AppStatus=1;";

    [Theory]
    [InlineData(null, null, GpuChoice.Windows)]
    [InlineData(PubgDefault, "2097", GpuChoice.Windows)]
    [InlineData(PubgAfterUserChange, "6193", GpuChoice.Windows)]
    [InlineData("AutoHDREnable=1;GpuPreference=2;", "1", GpuChoice.HighPerformance)]
    [InlineData("GpuPreference=1;", null, GpuChoice.PowerSaving)]
    [InlineData("GpuPreference=0;", null, GpuChoice.Windows)]
    public void Reads_the_current_state(string? value, string? autoHdrRaw, GpuChoice gpu)
    {
        Assert.Equal(new GameGraphicsState(autoHdrRaw, gpu), GameGraphics.Read(value));
    }

    [Fact]
    public void Never_touches_auto_hdr_and_keeps_windows_own_pairs()
    {
        Assert.Equal("SwapEffectUpgradeEnable=1;AutoHDREnable=6193;AppStatus=1;GpuPreference=2;",
            GameGraphics.Build(PubgAfterUserChange, GpuChoice.HighPerformance));
        Assert.Equal("AppStatus=1;AutoHDREnable=2097;GpuPreference=1;", GameGraphics.Build(PubgDefault, GpuChoice.PowerSaving));
        Assert.Equal(PubgDefault, GameGraphics.Build(PubgDefault, GpuChoice.Windows));
        Assert.Equal("GpuPreference=2;", GameGraphics.Build(null, GpuChoice.HighPerformance));
    }

    [Fact]
    public void Targets_every_entry_of_the_same_exe_plus_the_normalized_path()
    {
        var existing = new[]
        {
            "DirectXUserGlobalSettings",
            @"C:\Program Files (x86)\Steam\steamapps\common\Scrap Mechanic\.\Release\ScrapMechanic.exe",
            @"A:\SteamLibrary\steamapps\common\Scrap Mechanic\.\Release\ScrapMechanic.exe",
            Pubg,
        };

        Assert.Equal([Pubg], GameGraphics.TargetNames(Pubg, existing));
        Assert.Equal(
            [@"A:\SteamLibrary\steamapps\common\Scrap Mechanic\.\Release\ScrapMechanic.exe", @"A:\SteamLibrary\steamapps\common\Scrap Mechanic\Release\ScrapMechanic.exe"],
            GameGraphics.TargetNames(@"A:\SteamLibrary\steamapps\common\Scrap Mechanic\Release\ScrapMechanic.exe", existing));
        Assert.Equal([@"D:\Jeux\Nouveau\jeu.exe"], GameGraphics.TargetNames(@"D:\Jeux\Nouveau\jeu.exe", existing));
    }

    [Fact]
    public void Change_writes_every_target_from_its_original_value()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var targets = new[]
        {
            new GraphicsTarget(Pubg, SettingValue.String(PubgDefault), SettingValue.String("AppStatus=1;AutoHDREnable=2097;GpuPreference=1;")), // déjà modifié par OptiGame
            new GraphicsTarget(@"D:\autre\TslGame.exe", SettingValue.Absent, SettingValue.Absent),
        };

        var change = GameGraphics.Change(id, "PUBG", targets, GpuChoice.HighPerformance);

        Assert.NotNull(change);
        Assert.Equal("game.graphics.11111111222233334444555555555555", change.Id);
        Assert.Equal("Graphismes de PUBG : carte graphique la plus puissante", change.Title);
        Assert.Equal(["AppStatus=1;AutoHDREnable=2097;GpuPreference=2;", "GpuPreference=2;"], change.Writes.Select(w => w.NewValue.Text));
        Assert.Equal(KnownSettings.GpuPreference(Pubg), change.Writes[0].Target);
        Assert.Contains("GpuPreference=1; → AppStatus=1;AutoHDREnable=2097;GpuPreference=2;", change.What);
        Assert.Contains("(aucun réglage) → GpuPreference=2;", change.What);
        Assert.False(change.RequiresAdmin);
    }

    [Fact]
    public void Leaving_it_to_windows_needs_no_change()
    {
        Assert.Null(GameGraphics.Change(Guid.NewGuid(), "PUBG", [], GpuChoice.Windows));
    }
}

public sealed class DisplayHdrInfoTests
{
    [Theory]
    [InlineData(0x3u, true, true)]    // Samsung LC34G55T, HDR activé (machine de dev)
    [InlineData(0x5u, false, false)]  // BenQ GW2470 : couleur étendue SDR imposée, pas de HDR (machine de dev)
    [InlineData(0x1u, true, false)]   // HDR possible mais désactivé
    [InlineData(0x0u, false, false)]
    public void Wide_color_management_is_not_hdr(uint value, bool supported, bool enabled)
    {
        Assert.Equal(new Abstractions.DisplayHdrInfo("écran", supported, enabled), Abstractions.DisplayHdrInfo.FromAdvancedColor("écran", value));
    }
}
