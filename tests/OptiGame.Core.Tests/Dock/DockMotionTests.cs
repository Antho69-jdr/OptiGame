using OptiGame.Core.Dock;

namespace OptiGame.Core.Tests.Dock;

public sealed class DockMotionTests
{
    private const double Omega = 24;
    private const double Frame = 1 / 60.0;

    [Fact]
    public void Spring_starts_gently_then_settles_exactly_without_overshoot()
    {
        var (value, velocity) = (1.0, 0.0);
        var steps = new List<double>();
        for (var i = 0; i < 60 && (value, velocity) != (1.6, 0); i++)
        {
            var previous = value;
            (value, velocity) = DockSpring.Step(value, velocity, 1.6, Frame, Omega);
            Assert.InRange(value, 1.0, 1.6);
            steps.Add(value - previous);
        }

        Assert.Equal((1.6, 0.0), (value, velocity)); // arrivé en moins d'une seconde, pile sur la cible
        Assert.True(steps[0] < steps[1], "départ progressif : la 2e image avance plus que la 1re (ease-in)");
        Assert.True(steps[^1] < steps[1], "arrivée ralentie (ease-out)");
    }

    [Fact]
    public void Spring_keeps_its_velocity_when_the_target_moves()
    {
        var (value, velocity) = (1.0, 0.0);
        for (var i = 0; i < 5; i++) (value, velocity) = DockSpring.Step(value, velocity, 1.6, Frame, Omega);
        Assert.True(velocity > 0);

        // Nouvelle cible plus basse : on ralentit d'abord au lieu de repartir brutalement en arrière.
        var (next, _) = DockSpring.Step(value, velocity, 1.2, Frame, Omega);
        Assert.True(next >= value);
    }

    [Fact]
    public void Spring_is_stable_with_long_frames_and_ignores_empty_ones()
    {
        Assert.Equal((1.0, 0.0), DockSpring.Step(1.6, 0, 1.0, 2.0, Omega));
        Assert.Equal((1.3, 0.5), DockSpring.Step(1.3, 0.5, 1.0, 0, Omega));
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(1, 30, 1)]      // moins d'une demi-case : on ne bouge pas
    [InlineData(1, 37, 2)]      // plus de la moitié de la case suivante (slot 74)
    [InlineData(1, -40, 0)]
    [InlineData(0, 1000, 3)]    // borné à la dernière case
    [InlineData(3, -1000, 0)]
    public void Target_index_follows_the_dragged_cover(int from, double offset, int expected)
    {
        Assert.Equal(expected, DockReorder.TargetIndex(from, offset, 74, 4));
    }

    [Fact]
    public void Dragged_cover_stays_within_the_dock()
    {
        Assert.Equal(-74, DockReorder.ClampOffset(1, -500, 74, 4));
        Assert.Equal(148, DockReorder.ClampOffset(1, 500, 74, 4));
        Assert.Equal(20, DockReorder.ClampOffset(1, 20, 74, 4));
        Assert.Equal(0, DockReorder.ClampOffset(0, 50, 74, 1));
    }

    [Fact]
    public void Neighbours_make_room_for_the_dragged_cover()
    {
        // Jaquette 1 amenée en 3 : les jaquettes 2 et 3 reculent d'une case, 0 ne bouge pas.
        Assert.Equal(new double[] { 0, 0, -74, -74 }, new[] { 0, 1, 2, 3 }.Select(i => i == 1 ? 0 : DockReorder.Shift(i, 1, 3, 74)));
        // Jaquette 3 amenée en 1 : les jaquettes 1 et 2 avancent d'une case.
        Assert.Equal(new double[] { 0, 74, 74, 0 }, new[] { 0, 1, 2, 3 }.Select(i => i == 3 ? 0 : DockReorder.Shift(i, 3, 1, 74)));
        Assert.Equal(0, DockReorder.Shift(2, 1, 1, 74));
    }
}
