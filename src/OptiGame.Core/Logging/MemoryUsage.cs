using System.Diagnostics;
using System.Globalization;

namespace OptiGame.Core.Logging;

/// <summary>
/// Mémoire du processus à un instant donné, répartie entre le tas .NET (données lues, objets) et le reste (WPF, images
/// décodées, pilotes, code natif) : sert à trouver ce qui occupe la mémoire, dans le journal et dans DiagDump --memory.
/// </summary>
/// <param name="WorkingSetBytes">Mémoire physique utilisée (proche de la colonne « Mémoire » du Gestionnaire des tâches).</param>
/// <param name="PrivateBytes">Mémoire réservée au processus (physique ou fichier d'échange).</param>
/// <param name="GcAllocatedBytes">Objets .NET alloués, déchets pas encore récupérés compris.</param>
/// <param name="GcCommittedBytes">Mémoire réservée par le ramasse-miettes .NET (souvent bien plus que les objets vivants).</param>
/// <param name="GcLargeObjectBytes">Gros objets .NET (≥ 85 Ko : tableaux d'octets des fichiers lus…) au dernier passage du ramasse-miettes.</param>
public sealed record MemoryUsage(long WorkingSetBytes, long PrivateBytes, long GcAllocatedBytes, long GcCommittedBytes, long GcLargeObjectBytes)
{
    private const int LargeObjectGeneration = 3;

    public static MemoryUsage Now()
    {
        using var process = Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo();
        var largeObjects = gc.GenerationInfo.Length > LargeObjectGeneration ? gc.GenerationInfo[LargeObjectGeneration].SizeAfterBytes : 0;
        return new MemoryUsage(process.WorkingSet64, process.PrivateMemorySize64, GC.GetTotalMemory(forceFullCollection: false),
            gc.TotalCommittedBytes, largeObjects);
    }

    /// <summary>Mémoire privée hors du tas .NET : rendu WPF, images décodées, pilotes, code (approximation).</summary>
    public long OutsideGcBytes => Math.Max(0, PrivateBytes - GcCommittedBytes);

    public string Describe() =>
        $"RAM {Mb(WorkingSetBytes)} Mo (privée {Mb(PrivateBytes)} Mo) · .NET : {Mb(GcAllocatedBytes)} Mo alloués, " +
        $"{Mb(GcCommittedBytes)} Mo réservés, dont gros objets {Mb(GcLargeObjectBytes)} Mo · hors .NET ≈ {Mb(OutsideGcBytes)} Mo";

    public static string Mb(long bytes) => (bytes / (1024.0 * 1024)).ToString("0", CultureInfo.InvariantCulture);
}
