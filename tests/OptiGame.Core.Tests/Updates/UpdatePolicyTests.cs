using OptiGame.Core.Updates;

namespace OptiGame.Core.Tests.Updates;

public sealed class UpdatePolicyTests
{
    [Fact]
    public void An_automatic_update_waits_for_a_quiet_moment()
    {
        Assert.Null(UpdatePolicy.WhyNotNow(inGame: false, windowShown: false, fullscreenApp: false));
        Assert.Equal("une partie est en cours", UpdatePolicy.WhyNotNow(inGame: true, windowShown: true, fullscreenApp: true));
        Assert.Equal("la fenêtre d'OptiGame est ouverte", UpdatePolicy.WhyNotNow(inGame: false, windowShown: true, fullscreenApp: false));
        Assert.Equal("une application est en plein écran", UpdatePolicy.WhyNotNow(inGame: false, windowShown: false, fullscreenApp: true));
    }

    [Theory]
    [InlineData("1.1.0", "1.2.0", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.3.0", "1.2.0", false)] // retour à une version plus ancienne : pas une mise à jour
    [InlineData(null, "1.2.0", false)]    // première exécution
    [InlineData("?", "1.2.0", false)]
    public void A_newer_version_at_startup_means_an_update_was_installed(string? lastRun, string current, bool expected) =>
        Assert.Equal(expected, UpdatePolicy.JustUpdated(lastRun, Version.Parse(current)));

    [Theory]
    // Clé de désinstallation d'Inno Setup : InstallLocation se termine par « \ » ; AppContext.BaseDirectory aussi.
    [InlineData(@"C:\Program Files\OptiGame\", @"C:\Program Files\OptiGame\", null)]
    [InlineData(@"c:\program files\optigame", @"C:\Program Files\OptiGame\", null)]
    [InlineData(null, @"C:\Users\a\OptiGame\src\OptiGame.App\bin\Debug\net10.0-windows\", "OptiGame n'a pas été installé par son installeur (copie de développement)")]
    [InlineData(@"C:\Program Files\OptiGame\", @"C:\Users\a\OptiGame\bin\", @"cette copie d'OptiGame (C:\Users\a\OptiGame\bin) n'est pas celle qui est installée (C:\Program Files\OptiGame)")]
    [InlineData(@"D:\Jeux\OptiGame\", @"D:\Jeux\OptiGame\", @"OptiGame est installé hors de Program Files (D:\Jeux\OptiGame), dans un dossier où d'autres programmes peuvent écrire")]
    [InlineData(@"C:\Program Files Evil\OptiGame\", @"C:\Program Files Evil\OptiGame\", @"OptiGame est installé hors de Program Files (C:\Program Files Evil\OptiGame), dans un dossier où d'autres programmes peuvent écrire")]
    public void Only_the_installed_copy_in_program_files_updates_itself(string? installLocation, string runningFrom, string? expected) =>
        Assert.Equal(expected, UpdatePolicy.WhyNoSelfUpdate(installLocation, runningFrom, @"C:\Program Files"));

    [Theory]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("10.0.12", "10.0.12")]
    [InlineData("1.2", null)]
    [InlineData("1.2.0.0", null)]
    [InlineData("v1.2.0", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_three_part_versions_are_read(string? text, string? expected) =>
        Assert.Equal(expected is null ? null : Version.Parse(expected), UpdatePolicy.Parse(text));
}
