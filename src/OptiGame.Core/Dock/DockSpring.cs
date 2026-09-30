namespace OptiGame.Core.Dock;

/// <summary>
/// Ressort à amortissement critique : une valeur rejoint sa cible sans dépasser, en partant en douceur (vitesse nulle
/// au départ, donc accélération progressive) et en ralentissant à l'arrivée. Si la cible bouge en cours de route
/// (souris qui glisse sur le dock), la vitesse est conservée : pas d'à-coup. Solution exacte, stable quel que soit
/// l'intervalle entre deux images.
/// </summary>
public static class DockSpring
{
    private const double SnapDistance = 0.0005;
    private const double SnapVelocity = 0.005;

    /// <param name="omega">Raideur (rad/s) : la valeur est quasiment arrivée au bout d'environ 5 / omega secondes.</param>
    public static (double Value, double Velocity) Step(double value, double velocity, double target, double elapsedSeconds, double omega)
    {
        if (elapsedSeconds <= 0 || omega <= 0) return (value, velocity);

        // x(t) = (x0 + (v0 + ω·x0)·t)·e^(−ωt), écart x mesuré par rapport à la cible.
        var x0 = value - target;
        var t = elapsedSeconds;
        var decay = Math.Exp(-omega * t);
        var b = velocity + omega * x0;
        var x = (x0 + b * t) * decay;
        var v = (velocity - omega * b * t) * decay;

        return Math.Abs(x) < SnapDistance && Math.Abs(v) < SnapVelocity ? (target, 0) : (target + x, v);
    }
}
