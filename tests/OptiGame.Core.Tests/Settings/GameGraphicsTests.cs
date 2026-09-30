using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Tests.Settings;

public sealed class GameGraphicsTests
{
    // Valeurs réelles de UserGpuPreferences sur la machine de dev (2026-09-30).
    private const string Pubg = @"A:\SteamLibrary\steamapps\common\PUBG\TslGame\Binaries\Win64\TslGame.exe";
    private const string PubgValue = "AppStatus=1;AutoHDREnable=2097;";

    [Theory]
    [InlineData(null, AutoHdrChoice.Windows, null, GpuChoice.Windows)]
    [InlineData("AppStatus=1;AutoHDREnable=2097;", AutoHdrChoice.Windows, "2097", GpuChoice.Windows)]
    [InlineData("AutoHDREnable=1;GpuPreference=2;", AutoHdrChoice.On, "1", GpuChoice.HighPerformance)]
    [InlineData("AutoHDREnable=0;GpuPreference=1;", AutoHdrChoice.Off, "0", GpuChoice.PowerSaving)]
    [InlineData("GpuPreference=0;", AutoHdrChoice.Windows, null, GpuChoice.Windows)]
    public void Reads_the_current_state(string? value, AutoHdrChoice hdr, string? raw, GpuChoice gpu)
    {
        Assert.Equal(new GameGraphicsState(hdr, raw, gpu), GameGraphics.Read(value));
    }

    [Fact]
    public void Builds_from_the_original_and_keeps_windows_own_pairs()
    {
        Assert.Equal("AppStatus=1;AutoHDREnable=1;", GameGraphics.Build(PubgValue, AutoHdrChoice.On, GpuChoice.Windows));
        Assert.Equal("AppStatus=1;AutoHDREnable=0;GpuPreference=2;", GameGraphics.Build(PubgValue, AutoHdrChoice.Off, GpuChoice.HighPerformance));
        // « Windows » garde la valeur d'origine telle quelle, même non documentée.
        Assert.Equal("AppStatus=1;AutoHDREnable=2097;GpuPreference=1;", GameGraphics.Build(PubgValue, AutoHdrChoice.Windows, GpuChoice.PowerSaving));
        Assert.Equal("AutoHDREnable=1;", GameGraphics.Build(null, AutoHdrChoice.On, GpuChoice.Windows));
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
            new GraphicsTarget(Pubg, SettingValue.String(PubgValue), SettingValue.String("AppStatus=1;AutoHDREnable=0;")), // déjà modifié par OptiGame
            new GraphicsTarget(@"D:\autre\TslGame.exe", SettingValue.Absent, SettingValue.Absent),
        };

        var change = GameGraphics.Change(id, "PUBG", targets, AutoHdrChoice.On, GpuChoice.Windows);

        Assert.NotNull(change);
        Assert.Equal("game.graphics.11111111222233334444555555555555", change.Id);
        Assert.Equal("Graphismes de PUBG : Auto HDR activé", change.Title);
        Assert.Equal(["AppStatus=1;AutoHDREnable=1;", "AutoHDREnable=1;"], change.Writes.Select(w => w.NewValue.Text));
        Assert.Equal(KnownSettings.GpuPreference(Pubg), change.Writes[0].Target);
        Assert.Contains("AppStatus=1;AutoHDREnable=0; → AppStatus=1;AutoHDREnable=1;", change.What);
        Assert.Contains("(aucun réglage) → AutoHDREnable=1;", change.What);
        Assert.False(change.RequiresAdmin);
    }

    [Fact]
    public void Leaving_everything_to_windows_needs_no_change()
    {
        Assert.Null(GameGraphics.Change(Guid.NewGuid(), "PUBG", [], AutoHdrChoice.Windows, GpuChoice.Windows));
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
