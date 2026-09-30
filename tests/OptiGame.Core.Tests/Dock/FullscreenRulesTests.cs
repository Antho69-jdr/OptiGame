using OptiGame.Core.Dock;

namespace OptiGame.Core.Tests.Dock;

public sealed class FullscreenRulesTests
{
    private static readonly ScreenRect Primary = new(0, 0, 3440, 1440);
    private static readonly ScreenRect Secondary = new(3440, 0, 4520, 1920);

    private static ForegroundWindowInfo Window(ScreenRect rect, ScreenRect monitor, bool onDockMonitor = true,
        bool maximized = false, bool caption = false, bool shell = false) =>
        new(shell, onDockMonitor, maximized, caption, rect, monitor);

    [Fact]
    public void Real_case_maximized_discord_on_second_screen_does_not_hide_the_dock()
    {
        // Relevé réel : Discord agrandi (déborde de 8 px), Windows renvoie QUNS_BUSY (2).
        var discord = Window(new ScreenRect(3432, -8, 4528, 1928), Secondary, onDockMonitor: false, maximized: true, caption: true);

        Assert.False(FullscreenRules.ShouldHideDock(notificationState: 2, discord));
    }

    [Fact]
    public void Maximized_window_on_the_dock_screen_is_not_fullscreen()
    {
        Assert.False(FullscreenRules.ShouldHideDock(1, Window(new ScreenRect(-8, -8, 3448, 1448), Primary, maximized: true, caption: true)));
    }

    [Fact]
    public void Borderless_fullscreen_game_on_the_dock_screen_hides_the_dock()
    {
        Assert.True(FullscreenRules.ShouldHideDock(1, Window(Primary, Primary)));
    }

    [Fact]
    public void Borderless_fullscreen_on_the_other_screen_does_not_hide_the_dock()
    {
        Assert.False(FullscreenRules.ShouldHideDock(1, Window(Secondary, Secondary, onDockMonitor: false)));
    }

    [Theory]
    [InlineData(3)] // Direct3D plein écran exclusif
    [InlineData(4)] // mode présentation
    public void Reliable_system_states_always_hide_the_dock(int state)
    {
        Assert.True(FullscreenRules.ShouldHideDock(state, foreground: null));
    }

    [Fact]
    public void Desktop_and_normal_windows_do_not_hide_the_dock()
    {
        Assert.False(FullscreenRules.ShouldHideDock(1, Window(Primary, Primary, shell: true)));
        Assert.False(FullscreenRules.ShouldHideDock(1, Window(new ScreenRect(100, 100, 1500, 900), Primary, caption: true)));
        Assert.False(FullscreenRules.ShouldHideDock(1, foreground: null));
    }
}
