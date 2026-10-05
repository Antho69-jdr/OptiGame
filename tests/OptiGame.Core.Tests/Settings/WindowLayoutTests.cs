using OptiGame.Core.Settings;

namespace OptiGame.Core.Tests.Settings;

public class WindowLayoutTests
{
    // Écran 1080p : 100 % = 1920 × 1032 de zone de travail ; 150 % = 1280 × 688 (unités WPF).
    private static readonly ScreenRect FullHd = new(0, 0, 1920, 1032);
    private static readonly ScreenRect FullHdAt150 = new(0, 0, 1280, 688);

    [Fact]
    public void Without_saved_placement_the_window_is_centered_at_its_preferred_size()
    {
        var bounds = WindowLayout.InitialBounds(null, FullHd, FullHd, 880, 560);
        Assert.Equal(new ScreenRect(340, 86, 1240, 860), bounds);
    }

    [Fact]
    public void At_150_percent_the_window_fits_in_90_percent_of_the_work_area()
    {
        var bounds = WindowLayout.InitialBounds(null, FullHdAt150, FullHdAt150, 880, 560);
        Assert.Equal(1152, bounds.Width);
        Assert.Equal(619.2, bounds.Height, 3);
        Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Bottom <= 688);
    }

    [Fact]
    public void Never_smaller_than_the_minimum_size()
    {
        var tiny = new ScreenRect(0, 0, 800, 500);
        var bounds = WindowLayout.InitialBounds(null, tiny, tiny, 880, 560);
        Assert.Equal(880, bounds.Width);
        Assert.Equal(560, bounds.Height);
    }

    [Fact]
    public void A_visible_saved_placement_is_kept()
    {
        var saved = new WindowPlacement { Left = 100, Top = 50, Width = 1000, Height = 700 };
        Assert.Equal(new ScreenRect(100, 50, 1000, 700), WindowLayout.InitialBounds(saved, FullHd, FullHd, 880, 560));
    }

    [Fact]
    public void A_saved_placement_on_an_unplugged_screen_is_ignored()
    {
        // Fenêtre laissée sur un second écran à droite, débranché depuis.
        var saved = new WindowPlacement { Left = 2200, Top = 100, Width = 1000, Height = 700 };
        var bounds = WindowLayout.InitialBounds(saved, FullHd, FullHd, 880, 560);
        Assert.Equal(new ScreenRect(340, 86, 1240, 860), bounds);
    }

    [Fact]
    public void Settings_clone_copies_the_placement_without_sharing_it()
    {
        var settings = new AppSettings
        {
            MainWindowPlacement = new WindowPlacement { Left = 1, Top = 2, Width = 3, Height = 4, Maximized = true },
            CloseToTrayExplained = true,
        };
        var copy = settings.Clone();
        copy.MainWindowPlacement!.Left = 99;
        Assert.Equal(1, settings.MainWindowPlacement.Left);
        Assert.True(copy.MainWindowPlacement.Maximized);
        Assert.True(copy.CloseToTrayExplained);
    }

    [Fact]
    public void A_title_bar_above_the_screen_is_not_kept()
    {
        var saved = new WindowPlacement { Left = 100, Top = -500, Width = 1000, Height = 700 };
        Assert.NotEqual(-500, WindowLayout.InitialBounds(saved, FullHd, FullHd, 880, 560).Top);
    }
}
