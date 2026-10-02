using OptiGame.Core.Library;

namespace OptiGame.Core.Tests.Library;

/// <summary>Réponses réelles des 4 disques de la machine de dev (2026-10-02, DiagDump --disks).</summary>
public sealed class GameDiskTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static readonly DiskFacts Nvme = new("A:", "KXG50ZNV512G TOSHIBA", 4, 17, false, true, 192 * Gb, 477 * Gb);
    private static readonly DiskFacts SataSsd = new("C:", "Samsung SSD 860 EVO 1TB", 4, 11, false, true, 154 * Gb, 243 * Gb);
    private static readonly DiskFacts HardDisk = new("D:", "WDC WD10EARX-00N0YB0", 0, 11, null, false, 236 * Gb, 932 * Gb);
    private static readonly DiskFacts Usb = new("E:", "IT-CEO USB2.0SATADevice", 0, 7, null, null, 338 * Gb, 466 * Gb);

    [Fact]
    public void Recognizes_the_dev_machine_disks()
    {
        Assert.Equal(DiskKind.Ssd, GameDisk.KindOf(Nvme));
        Assert.Equal(DiskKind.Ssd, GameDisk.KindOf(SataSsd));
        Assert.Equal(DiskKind.Hdd, GameDisk.KindOf(HardDisk)); // déclaré « non précisé » : seul TRIM le révèle
        Assert.Equal(DiskKind.Unknown, GameDisk.KindOf(Usb));
        Assert.Equal(DiskKind.Hdd, GameDisk.KindOf(Usb with { SeekPenalty = true }));
        Assert.Equal(DiskKind.Hdd, GameDisk.KindOf(Nvme with { MediaType = 3 })); // type déclaré prioritaire
    }

    [Fact]
    public void A_game_on_an_ssd_with_room_is_fine()
    {
        var report = GameDisk.Assess(Nvme, isSteamGame: true);
        Assert.Equal((GameDiskLevel.Ok, "Bon emplacement."), (report.Level, report.Summary));
        Assert.Equal("SSD NVMe A: (KXG50ZNV512G TOSHIBA) · 192 Go libres sur 477 Go", report.Detail);
        Assert.Empty(report.Advice);
    }

    [Fact]
    public void A_game_on_a_hard_disk_gets_the_way_to_move_it()
    {
        var steam = GameDisk.Assess(HardDisk, isSteamGame: true);
        Assert.Equal((GameDiskLevel.Warning, "Installé sur un disque dur."), (steam.Level, steam.Summary));
        Assert.Contains(steam.Advice, a => a.Contains("Déplacer le dossier d'installation"));

        var other = GameDisk.Assess(HardDisk, isSteamGame: false);
        Assert.Contains(other.Advice, a => a.Contains("son lanceur"));
    }

    [Fact]
    public void A_nearly_full_disk_is_flagged()
    {
        var full = GameDisk.Assess(SataSsd with { FreeBytes = 15 * Gb }, isSteamGame: true);
        Assert.Equal((GameDiskLevel.Warning, "Disque presque plein."), (full.Level, full.Summary));

        var both = GameDisk.Assess(HardDisk with { FreeBytes = 50 * Gb }, isSteamGame: true); // 50 Go < 10 % de 932 Go
        Assert.Equal("Installé sur un disque dur et disque presque plein.", both.Summary);
    }

    [Fact]
    public void External_and_network_drives_are_only_information()
    {
        var usb = GameDisk.Assess(Usb, isSteamGame: false);
        Assert.Equal((GameDiskLevel.Info, "À surveiller."), (usb.Level, usb.Summary));
        Assert.StartsWith("Disque externe USB (type inconnu) E:", usb.Detail);

        var network = GameDisk.Assess(new DiskFacts(@"\\nas\jeux\", null, null, null, null, null, 0, 0, IsNetwork: true), false);
        Assert.Equal(GameDiskLevel.Info, network.Level);
        Assert.StartsWith("Lecteur réseau", network.Detail);
    }
}
