using OptiGame.Core.Changes;
using OptiGame.Core.State;

namespace OptiGame.Core.Diagnostics;

/// <summary>Vue « Simple » du Diagnostic : ce que fait le bouton unique, et ce qu'il laisse à l'utilisateur.</summary>
/// <param name="Recommended">Corrections que « Activer les optimisations » appliquera (après UNE confirmation listant chacune).</param>
/// <param name="ManualActions">Points à corriger sans correction automatique (BIOS, pilote…) : à faire soi-même.</param>
/// <param name="Optional">Contrôles « Info » proposant une correction facultative : vue « Avancé » seulement.</param>
/// <param name="Applied">Corrections du Diagnostic actives, annulées par « Désactiver les optimisations ».</param>
public sealed record OptimizationOverview(
    IReadOnlyList<ReversibleChange> Recommended,
    IReadOnlyList<DiagnosticResult> ManualActions,
    IReadOnlyList<DiagnosticResult> Optional,
    IReadOnlyList<ChangeRecord> Applied,
    int OkCount,
    int AttentionCount,
    int InfoCount)
{
    public bool IsOptimized => Recommended.Count == 0 && ManualActions.Count == 0;
}

/// <summary>
/// Règles du bouton unique : seulement les corrections des contrôles « À corriger », jamais les options avancées (ex. intégrité
/// de la mémoire) ni les corrections facultatives des contrôles « Info » (HAGS, GPU par application, jeux fenêtrés par défaut).
/// « Désactiver » n'annule que les corrections du Diagnostic (identifiants « fix. »), jamais les réglages par jeu
/// (plafond NVIDIA, carte graphique : « game. »), qui vivent dans le même journal.
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
        return new OptimizationOverview(recommended, manual, optional, applied,
            results.Count(r => r.Status == DiagnosticStatus.Ok), attention.Count, results.Count(r => r.Status == DiagnosticStatus.Info));
    }
}
