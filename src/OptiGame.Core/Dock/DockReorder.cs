namespace OptiGame.Core.Dock;

/// <summary>
/// Réorganisation du dock par glisser-déposer : la jaquette tenue suit la souris le long du dock (sans en sortir), et
/// les autres s'écartent d'une case pour montrer où elle arrivera. <c>slot</c> = place d'une icône au repos (marges comprises).
/// </summary>
public static class DockReorder
{
    /// <summary>Décalage de la jaquette tenue, borné entre la première et la dernière case.</summary>
    public static double ClampOffset(int fromIndex, double offset, double slot, int count) =>
        count <= 1 || slot <= 0 ? 0 : Math.Clamp(offset, -fromIndex * slot, (count - 1 - fromIndex) * slot);

    /// <summary>Case d'arrivée : celle dont la jaquette tenue a dépassé la moitié.</summary>
    public static int TargetIndex(int fromIndex, double offset, double slot, int count)
    {
        if (count <= 0 || slot <= 0) return fromIndex;
        var steps = (int)Math.Round(offset / slot, MidpointRounding.AwayFromZero);
        return Math.Clamp(fromIndex + steps, 0, count - 1);
    }

    /// <summary>Décalage d'une autre jaquette pour libérer la case d'arrivée (0, +slot ou −slot).</summary>
    public static double Shift(int index, int fromIndex, int targetIndex, double slot)
    {
        if (fromIndex < targetIndex && index > fromIndex && index <= targetIndex) return -slot;
        if (targetIndex < fromIndex && index >= targetIndex && index < fromIndex) return slot;
        return 0;
    }
}
