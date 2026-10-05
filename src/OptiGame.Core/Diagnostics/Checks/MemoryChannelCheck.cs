using System.Text.RegularExpressions;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Text;

namespace OptiGame.Core.Diagnostics.Checks;

/// <summary>
/// Estime le mode multi-canal à partir des libellés de slots (BankLabel / DeviceLocator). Windows n'expose pas
/// directement le mode réel : le résultat est toujours signalé comme une estimation.
/// </summary>
public sealed partial class MemoryChannelCheck(IMemoryInfoProvider memory) : IDiagnosticCheck
{
    public string Id => "memory.channels";

    public string Title => "Barrettes de RAM et dual-channel";

    public DiagnosticResult Run()
    {
        var info = memory.GetMemoryInfo();
        var modules = info.Modules;
        if (modules.Count == 0)
        {
            return Result(DiagnosticStatus.Info, "Informations mémoire indisponibles.", "", []);
        }

        var channels = modules.Select(m => ChannelOf(m)).ToList();
        var details = modules.Select((m, i) =>
            $"{m.BankLabel} {m.DeviceLocator}".Trim() + (channels[i] is { } c ? $" → canal {c}" : " → canal non identifiable")).ToList();
        if (info.TotalSlots is { } slots)
        {
            details.Add($"{FrenchText.Count(modules.Count, "barrette", "barrettes")} sur {FrenchText.Count(slots, "emplacement", "emplacements")}.");
        }

        if (modules.Count == 1)
        {
            return Result(DiagnosticStatus.NeedsAttention,
                "Une seule barrette : mode single-channel probable.",
                "Avec une seule barrette, la mémoire fonctionne en général en single-channel : la bande passante est " +
                "divisée par deux, ce qui pénalise les 1 % low et fortement les GPU intégrés. Ajouter une barrette " +
                "identique (même capacité, même vitesse) dans l'emplacement recommandé par le manuel de la carte mère " +
                "active le dual-channel. Exception : certains portables ont une partie de la mémoire soudée.",
                details);
        }

        var known = channels.Where(c => c is not null).Distinct().ToList();
        if (known.Count >= 2)
        {
            return Result(DiagnosticStatus.Ok,
                $"{modules.Count} barrettes réparties sur {known.Count} canaux ({string.Join(", ", known)}) : dual-channel probable.",
                "", details);
        }

        if (known.Count == 1 && channels.All(c => c is not null))
        {
            return Result(DiagnosticStatus.NeedsAttention,
                $"Toutes les barrettes semblent sur le canal {known[0]}.",
                "Les barrettes paraissent installées sur le même canal mémoire, ce qui empêcherait le dual-channel. " +
                "Vérifiez dans le manuel de la carte mère les emplacements à utiliser (souvent A2 et B2).",
                details);
        }

        return Result(DiagnosticStatus.Info,
            $"{modules.Count} barrettes : dual-channel probable si elles sont dans les emplacements recommandés.",
            "Les libellés des emplacements ne permettent pas d'identifier les canaux sur ce PC. Vérifiez dans le " +
            "manuel de la carte mère que les barrettes sont dans les emplacements recommandés.",
            details);
    }

    internal static string? ChannelOf(MemoryModule module)
    {
        var text = $"{module.BankLabel} {module.DeviceLocator}";
        var match = ChannelPattern().Match(text);
        if (match.Success) return match.Groups[1].Value.ToUpperInvariant();
        match = DimmPattern().Match(text);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    // « P0 CHANNEL A », « ChannelA-DIMM0 », « Controller0-ChannelB »
    [GeneratedRegex(@"CHANNEL\s*-?\s*([A-H])(?![A-Z])", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelPattern();

    // « DIMM_A1 », « DIMM B2 »
    [GeneratedRegex(@"DIMM[_\s-]?([A-H])\d", RegexOptions.IgnoreCase)]
    private static partial Regex DimmPattern();

    private DiagnosticResult Result(DiagnosticStatus status, string summary, string explanation, IReadOnlyList<string> details) => new()
    {
        CheckId = Id,
        Title = Title,
        Status = status,
        Summary = summary,
        Explanation = explanation,
        Details = details,
        IsEstimate = true,
    };
}
