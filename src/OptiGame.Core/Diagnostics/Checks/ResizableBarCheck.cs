using OptiGame.Core.Abstractions;
using OptiGame.Core.Gpu;

namespace OptiGame.Core.Diagnostics.Checks;

/// <summary>
/// Resizable BAR : le processeur accède à toute la mémoire vidéo d'un coup (fenêtre BAR1 = mémoire vidéo) au lieu de 256 Mio.
/// Lu seulement pour les cartes NVIDIA (nvidia-smi) : la lecture générique des plages mémoire de Windows (Win32_DeviceMemoryAddress)
/// ne voit PAS la fenêtre de 8 Gio de la RTX 3070 de la machine de dev (2026-10-03), elle n'est donc pas utilisée. Ne se
/// change que dans le BIOS : information et marche à suivre, jamais de correction. Pris en charge par NVIDIA à partir des RTX 30
/// (Ampere) : une fenêtre de 256 Mio est normale sur une carte plus ancienne.
/// </summary>
public sealed class ResizableBarCheck(IGpuInfoProvider gpus, INvidiaInfoProvider nvidia) : IDiagnosticCheck
{
    /// <summary>Taille de la fenêtre BAR1 sans Resizable BAR.</summary>
    public const int ClassicBarMib = 256;

    private static readonly string[] SupportedArchitectures = ["Ampere", "Ada", "Blackwell"];

    public string Id => "gpu.resizable-bar";

    public string Title => "Resizable BAR";

    public DiagnosticResult Run()
    {
        var adapters = gpus.GetAdapters().Where(a => a.IsPhysical).ToList();
        var hasNvidia = adapters.Any(a => Vendor(a) == "NVIDIA");
        var others = adapters.Where(a => Vendor(a) != "NVIDIA").ToList();
        IReadOnlyList<NvidiaGpuMemory>? memory = hasNvidia ? nvidia.GetMemory() : [];
        var details = new List<string>();
        var disabled = new List<string>();
        var enabled = 0;

        foreach (var gpu in memory ?? [])
        {
            var on = gpu.Bar1Mib > ClassicBarMib;
            var supported = gpu.Architecture is { } arch && SupportedArchitectures.Any(s => arch.Contains(s, StringComparison.OrdinalIgnoreCase));
            details.Add($"{gpu.Name} ({gpu.Architecture ?? "architecture inconnue"}) : fenêtre BAR1 {gpu.Bar1Mib?.ToString() ?? "?"} Mio " +
                        $"pour {gpu.VramMib?.ToString() ?? "?"} Mio de mémoire vidéo → {(on ? "activé" : supported ? "désactivé" : "non pris en charge par cette carte")}.");
            if (on) enabled++;
            else if (supported) disabled.Add(gpu.Name);
        }
        if (hasNvidia && memory is null) details.Add("nvidia-smi (installé avec le pilote NVIDIA) introuvable ou sans réponse : état non lu.");
        foreach (var other in others)
        {
            details.Add($"{other.Name} : état non lisible par OptiGame — voyez {(Vendor(other) == "AMD" ? "AMD Software (« Smart Access Memory »)" : Vendor(other) == "Intel" ? "Intel Graphics Software / Arc Control" : "l'outil du fabricant")}.");
        }

        if (disabled.Count > 0)
        {
            return Result(DiagnosticStatus.NeedsAttention, $"Désactivé ({string.Join(", ", disabled)}).",
                "Votre carte prend en charge Resizable BAR, mais il est désactivé : selon les jeux, 0 à 10 % de FPS en moins (NVIDIA ne " +
                "l'utilise que pour les jeux de la liste de son pilote). Il s'active uniquement dans le BIOS de la carte mère : « Above 4G " +
                "Decoding » puis « Re-Size BAR Support » (noms variables selon le fabricant). Conditions : BIOS en mode UEFI (CSM désactivé) " +
                "et disque système en GPT. OptiGame ne peut pas le modifier.", details);
        }
        if (enabled > 0 && others.Count == 0)
        {
            return Result(DiagnosticStatus.Ok, "Activé.",
                "Le processeur accède à toute la mémoire vidéo en une fois ; le pilote l'utilise pour les jeux qui en profitent.", details);
        }
        if (memory is { Count: > 0 } && enabled == 0 && others.Count == 0)
        {
            return Result(DiagnosticStatus.Info, "Non pris en charge par cette carte.",
                "Chez NVIDIA, Resizable BAR est pris en charge à partir des RTX 30 : une fenêtre de 256 Mio est normale ici.", details);
        }
        return Result(DiagnosticStatus.Info, "État non lu.",
            "OptiGame lit cet état sur les cartes NVIDIA. Pour les autres, vérifiez dans le logiciel du fabricant ; il s'active dans le BIOS " +
            "(« Above 4G Decoding » puis « Re-Size BAR Support », BIOS en mode UEFI).", details);
    }

    private static string Vendor(GpuAdapter adapter) =>
        adapter.PnpDeviceId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
        : adapter.PnpDeviceId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) ? "AMD"
        : adapter.PnpDeviceId.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) ? "Intel"
        : "autre";

    private DiagnosticResult Result(DiagnosticStatus status, string summary, string explanation, IReadOnlyList<string> details) => new()
    {
        CheckId = Id,
        Title = Title,
        Status = status,
        Summary = summary,
        Explanation = explanation,
        Details = details,
        Fixes = [],
    };
}
