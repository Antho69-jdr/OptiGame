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

    [Fact]
    public void Animations_follow_windows_by_default_and_survive_cloning()
    {
        var settings = new AppSettings();
        Assert.Equal(UiAnimations.FollowWindows, settings.UiAnimations);
        settings.UiAnimations = UiAnimations.Always;
        Assert.Equal(UiAnimations.Always, settings.Clone().UiAnimations);
    }
}
