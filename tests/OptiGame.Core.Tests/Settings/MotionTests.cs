using OptiGame.Core.Settings;

namespace OptiGame.Core.Tests.Settings;

public sealed class MotionTests
{
    [Theory]
    [InlineData(UiAnimations.FollowWindows, true, true)]
    [InlineData(UiAnimations.FollowWindows, false, false)]
    [InlineData(UiAnimations.Always, false, true)] // AtlasOS coupe les effets de Windows : OptiGame anime quand même
    [InlineData(UiAnimations.Never, true, false)]
    public void Animations_follow_windows_unless_overridden(UiAnimations mode, bool windows, bool expected) =>
        Assert.Equal(expected, Motion.IsEnabled(mode, windows));

    [Theory]
    [InlineData(8, 8)]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    [InlineData(80, 24)]
    public void Cover_radius_is_clamped(int input, int expected) => Assert.Equal(expected, CoverStyle.Radius(input));

    [Fact]
    public void Cover_radius_defaults_to_the_theme_value_and_survives_cloning()
    {
        var settings = new AppSettings();
        Assert.Equal(8, settings.CoverCornerRadius);
        settings.CoverCornerRadius = 16;
        Assert.Equal(16, settings.Clone().CoverCornerRadius);
    }

    [Fact]
    public void Animations_follow_windows_by_default_and_survive_cloning()
    {
        var settings = new AppSettings();
        Assert.Equal(UiAnimations.FollowWindows, settings.UiAnimations);
        settings.UiAnimations = UiAnimations.Always;
        Assert.Equal(UiAnimations.Always, settings.Clone().UiAnimations);
    }
}
