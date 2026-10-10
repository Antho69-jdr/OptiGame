using OptiGame.Core.Updates;

namespace OptiGame.Core.Tests.Updates;

public sealed class AutoStartTargetTests
{
    private const string Installed = @"C:\Program Files\OptiGame\OptiGame.exe";
    private const string DevCopy = @"C:\Users\antho\Projets Appli\OptiGame\src\OptiGame.App\bin\Debug\net10.0-windows\OptiGame.exe";

    [Fact]
    public void The_installed_copy_takes_back_a_task_that_starts_another_copy() =>
        Assert.True(AutoStartTarget.ShouldRepoint(DevCopy, Installed, runningIsInstalledCopy: true)); // cas réel du 2026-10-10

    [Fact]
    public void A_development_copy_never_touches_the_task() =>
        Assert.False(AutoStartTarget.ShouldRepoint(Installed, DevCopy, runningIsInstalledCopy: false));

    [Fact]
    public void Nothing_to_do_when_the_task_already_starts_this_copy_or_does_not_exist()
    {
        Assert.False(AutoStartTarget.ShouldRepoint("\"c:\\program files\\optigame\\OptiGame.exe\"", Installed, runningIsInstalledCopy: true));
        Assert.False(AutoStartTarget.ShouldRepoint(null, Installed, runningIsInstalledCopy: true)); // désactivé : jamais créée ici
    }
}
