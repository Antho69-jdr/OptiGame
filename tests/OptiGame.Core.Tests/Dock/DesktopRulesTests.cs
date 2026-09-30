using OptiGame.Core.Dock;

namespace OptiGame.Core.Tests.Dock;

public sealed class DesktopRulesTests
{
    private static readonly WindowAbove App = new(Visible: true, Cloaked: false, Minimized: false, Topmost: false, ToolWindow: false, HasArea: true);

    [Fact]
    public void Win_d_shows_the_desktop_when_no_application_is_above_it()
    {
        // Après Win+D : il ne reste au-dessus du bureau que des fenêtres qui ne le recouvrent pas.
        var above = new[]
        {
            App with { Minimized = true },
            App with { Cloaked = true },
            App with { Visible = false },
            App with { Topmost = true },     // barre des tâches
            App with { ToolWindow = true },  // RocketDock, dock d'OptiGame
            App with { HasArea = false },
        };
        Assert.True(DesktopRules.IsDesktopShown(desktopIsForeground: true, above));
        Assert.True(DesktopRules.IsDesktopShown(desktopIsForeground: true, []));
    }

    [Fact]
    public void Clicking_the_wallpaper_between_windows_does_not_count()
    {
        Assert.False(DesktopRules.IsDesktopShown(desktopIsForeground: true, [App with { Minimized = true }, App]));
    }

    [Fact]
    public void Desktop_must_be_in_the_foreground()
    {
        Assert.False(DesktopRules.IsDesktopShown(desktopIsForeground: false, []));
    }
}
