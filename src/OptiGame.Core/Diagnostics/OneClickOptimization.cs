using OptiGame.Core.Changes;
using OptiGame.Core.State;

namespace OptiGame.Core.Diagnostics;

/// <summary>Verdict de l'en-tête du Diagnostic (jamais « prêt » si un contrôle n'a pas pu s'exécuter).</summary>
public enum DiagnosticVerdict
{
    /// <summary>Des optimisations sont proposées (bouton principal).</summary>
    Recommended,

    /// <summary>Rien à appliquer par OptiGame, mais des points à corriger soi-même (BIOS, pilote, option avancée).</summary>
    ManualActions,

    /// <summary>Rien à corriger parmi ce qui a été vérifié, mais au moins un contrôle n'a pas pu s'exécuter.</summary>
    Incomplete,

    /// <summary>Tout a été vérifié, rien à corriger.</summary>
    Ready,
}

/// <summary>Page Diagnostic : ce que fait le bouton unique, et ce qu'il laisse à l'utilisateur.</summary>
/// <param name="Recommended">Optimisations que « Appliquer les N optimisations… » appliquera (après UNE confirmation listant chacune).</param>
/// <param name="ManualActions">Points à corriger sans optimisation automatique (BIOS, pilote…) : à faire soi-même.</param>
/// <param name="Optional">Contrôles « À savoir » proposant une optimisation facultative : appliquée seulement à la main.</param>
/// <param name="Applied">Optimisations du Diagnostic actives, restaurées par « Tout restaurer… ».</param>
/// <param name="Unverified">Contrôles qui n'ont pas pu s'exécuter (« Non vérifié »).</param>
public sealed record OptimizationOverview(
    IReadOnlyList<ReversibleChange> Recommended,
    IReadOnlyList<DiagnosticResult> ManualActions,
    IReadOnlyList<DiagnosticResult> Optional,
    IReadOnlyList<ChangeRecord> Applied,
    int OkCount,
    int AttentionCount,
    int InfoCount,
    IReadOnlyList<DiagnosticResult> Unverified)
{
    /// <summary>Rien à appliquer ni à faire soi-même (les contrôles non vérifiés comptent à part : voir <see cref="Verdict"/>).</summary>
    public bool IsOptimized => Recommended.Count == 0 && ManualActions.Count == 0;

    public DiagnosticVerdict Verdict =>
        Recommended.Count > 0 ? DiagnosticVerdict.Recommended
        : ManualActions.Count > 0 ? DiagnosticVerdict.ManualActions
        : Unverified.Count > 0 ? DiagnosticVerdict.Incomplete
        : DiagnosticVerdict.Ready;
}

/// <summary>
/// Règles du bouton unique : seulement les optimisations des contrôles « À corriger », jamais les options avancées (ex.
/// intégrité de la mémoire) ni les optimisations facultatives des contrôles « À savoir » (HAGS, GPU par application, jeux
/// fenêtrés par défaut). « Tout restaurer… » ne restaure que les optimisations du Diagnostic (identifiants « fix. »), jamais
/// les réglages par jeu (plafond NVIDIA, carte graphique : « game. »), qui vivent dans le même journal.
/// </summary>
public static class OneClickOptimization
{
    public const string DiagnosticFixPrefix = "fix.";

    public static OptimizationOverview Overview(IReadOnlyList<DiagnosticResult> results, IReadOnlyList<ChangeRecord> activeChanges)
    {
        var active = activeChanges.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var attention = results.Where(r => r.Status == DiagnosticStatus.NeedsAttention).ToList();
        var recommended = attention
            .SelectMany(r => r.Fixes)
            .Where(f => !f.IsAdvanced && !active.Contains(f.Change.Id))
            .Select(f => f.Change)
            .ToList();
        var manual = attention.Where(r => !r.Fixes.Any(f => !f.IsAdvanced)).ToList();
        var optional = results
            .Where(r => r.Status == DiagnosticStatus.Info && r.Fixes.Any(f => !f.IsAdvanced && !active.Contains(f.Change.Id)))
            .ToList();
        var applied = activeChanges.Where(c => c.Id.StartsWith(DiagnosticFixPrefix, StringComparison.Ordinal)).ToList();
        var unverified = results.Where(r => r.Status == DiagnosticStatus.Error).ToList();
        return new OptimizationOverview(recommended, manual, optional, applied,
            results.Count(r => r.Status == DiagnosticStatus.Ok), attention.Count, results.Count(r => r.Status == DiagnosticStatus.Info), unverified);
    }
}
