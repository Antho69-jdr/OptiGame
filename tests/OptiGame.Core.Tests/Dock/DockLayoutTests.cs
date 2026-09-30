using OptiGame.Core.Dock;
using OptiGame.Core.Settings;

namespace OptiGame.Core.Tests.Dock;

public sealed class DockLayoutTests
{
    [Theory]
    [InlineData(64, DockIconShape.Square, 64, 64)]
    [InlineData(64, DockIconShape.Cover, 64, 85)]
    [InlineData(10, DockIconShape.Square, 32, 32)]
    [InlineData(500, DockIconShape.Cover, 128, 171)]
    public void Icon_size_follows_shape_and_is_clamped(int size, DockIconShape shape, double width, double height)
    {
        Assert.Equal((width, height), DockLayout.IconSize(size, shape));
    }

    [Theory]
    [InlineData(0.9, 0.9)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(3, 1)]
    [InlineData(double.NaN, 0.9)]
    public void Opacity_is_clamped(double input, double expected)
    {
        Assert.Equal(expected, DockLayout.Opacity(input));
    }

    [Theory]
    [InlineData(1.0, 255, 64)]
    [InlineData(0.5, 128, 32)]
    [InlineData(0.01, 3, 1)]
    [InlineData(0.0, 1, 0)] // invisible mais jamais 0 : le dock garde la souris entre les icônes
    public void Plate_alpha_follows_opacity_and_never_reaches_zero(double opacity, byte background, byte border)
    {
        Assert.Equal((background, border), DockLayout.PlateAlpha(opacity));
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
