using OptiGame.Core.Dock;

namespace OptiGame.Core.Tests.Dock;

public sealed class DockMagnificationTests
{
    [Fact]
    public void Maximal_under_the_mouse_and_normal_beyond_range()
    {
        Assert.Equal(1.6, DockMagnification.Scale(0, 140, 1.6), 6);
        Assert.Equal(1.0, DockMagnification.Scale(140, 140, 1.6), 6);
        Assert.Equal(1.0, DockMagnification.Scale(500, 140, 1.6), 6);
    }

    [Fact]
    public void Symmetric_and_decreasing_with_distance()
    {
        Assert.Equal(DockMagnification.Scale(-50, 140, 1.6), DockMagnification.Scale(50, 140, 1.6), 9);
        var previous = double.MaxValue;
        for (var d = 0; d <= 140; d += 10)
        {
            var scale = DockMagnification.Scale(d, 140, 1.6);
            Assert.True(scale <= previous);
            Assert.InRange(scale, 1.0, 1.6);
            previous = scale;
        }
        Assert.Equal(1.3, DockMagnification.Scale(70, 140, 1.6), 6); // mi-distance = mi-grossissement
    }

    [Fact]
    public void Degenerate_parameters_disable_the_effect()
    {
        Assert.Equal(1.0, DockMagnification.Scale(0, 0, 1.6));
        Assert.Equal(1.0, DockMagnification.Scale(0, 140, 0.9));
    }
}
