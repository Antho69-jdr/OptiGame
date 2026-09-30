using OptiGame.Core.Dock;
using OptiGame.Core.Settings;

namespace OptiGame.Core.Tests.Dock;

public sealed class DockLayoutTests
{
    [Theory]
    [InlineData(64, DockIconShape.Square, 64, 64)]
    [InlineData(64, DockIconShape.Cover, 64, 96)]
    [InlineData(10, DockIconShape.Square, 32, 32)]
    [InlineData(500, DockIconShape.Cover, 128, 192)]
    public void Icon_size_follows_shape_and_is_clamped(int size, DockIconShape shape, double width, double height)
    {
        Assert.Equal((width, height), DockLayout.IconSize(size, shape));
    }

    [Theory]
    [InlineData(0.9, 0.9)]
    [InlineData(0.05, 0.2)]
    [InlineData(3, 1)]
    [InlineData(double.NaN, 0.9)]
    public void Opacity_is_clamped(double input, double expected)
    {
        Assert.Equal(expected, DockLayout.Opacity(input));
    }

    [Fact]
    public void New_dock_settings_have_sensible_defaults()
    {
        var settings = new AppSettings();
        Assert.Equal((64, DockIconShape.Square, 0.9), (settings.DockIconSize, settings.DockIconShape, settings.DockOpacity));
        var clone = settings.Clone();
        clone.DockOpacity = 0.5;
        Assert.Equal(0.9, settings.DockOpacity);
    }
}
