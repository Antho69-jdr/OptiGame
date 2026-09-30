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
        Assert.Equal((1.0, 0.0), DockMagnification.Place(100, 74, 130, 140, 1.0));
    }

    [Fact]
    public void Stretch_is_the_integral_of_the_scale()
    {
        foreach (var u in new[] { -300.0, -140, -70, -10, 0, 25, 70, 139, 140, 400 })
        {
            // Intégrale numérique (méthode du point milieu) de la densité entre la souris et u.
            const int n = 20000;
            var sum = 0.0;
            for (var i = 0; i < n; i++) sum += DockMagnification.Scale(u * (i + 0.5) / n, 140, 1.6) * u / n;
            Assert.Equal(sum, DockMagnification.Stretch(u, 140, 1.6), 3);
        }
        Assert.Equal(-DockMagnification.Stretch(55, 140, 1.6), DockMagnification.Stretch(-55, 140, 1.6), 9);
    }

    [Fact]
    public void Magnified_slots_stay_contiguous_and_the_point_under_the_mouse_stays_put()
    {
        const double slot = 74;
        foreach (var mouse in new[] { -50.0, 0, 37, 60, 111, 150, 296, 400 })
        {
            var edges = new List<(double Left, double Right)>();
            for (var i = 0; i < 4; i++)
            {
                var (scale, shift) = DockMagnification.Place(i * slot, slot, mouse, 140, 1.6);
                var center = i * slot + slot / 2 + shift;
                edges.Add((center - slot * scale / 2, center + slot * scale / 2));
            }
            for (var i = 1; i < 4; i++) Assert.Equal(edges[i - 1].Right, edges[i].Left, 9); // ni trou ni chevauchement
            Assert.Equal(mouse, DockMagnification.Map(mouse, mouse, 140, 1.6), 9);
        }
    }

    [Fact]
    public void Small_mouse_moves_give_small_changes()
    {
        // Balayage rapide : d'un pixel à l'autre, aucune case ne saute (c'était le cas quand le dock se recentrait).
        for (var mouse = -100.0; mouse < 400; mouse += 1)
        {
            for (var i = 0; i < 4; i++)
            {
                var (s1, d1) = DockMagnification.Place(i * 74, 74, mouse, 140, 1.6);
                var (s2, d2) = DockMagnification.Place(i * 74, 74, mouse + 1, 140, 1.6);
                Assert.True(Math.Abs(d2 - d1) < 1.5, $"décalage de la case {i} en {mouse}");
                Assert.True(Math.Abs(s2 - s1) < 0.02, $"échelle de la case {i} en {mouse}");
            }
        }
    }
}
