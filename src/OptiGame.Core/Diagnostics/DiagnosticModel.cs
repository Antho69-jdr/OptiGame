using OptiGame.Core.Changes;

namespace OptiGame.Core.Diagnostics;

public enum DiagnosticStatus
{
    Ok,
    NeedsAttention,
    Info,

    /// <summary>Le contrôle n'a pas pu s'exécuter.</summary>
    Error,
}

/// <summary>Correction proposée ; n'est jamais appliquée sans confirmation explicite.</summary>
/// <param name="IsAdvanced">Option avancée : affichée à part, avec double confirmation.</param>
public sealed record DiagnosticFix(ReversibleChange Change, bool IsAdvanced = false);

public sealed record DiagnosticResult
{
    public required string CheckId { get; init; }

    public required string Title { get; init; }

    public required DiagnosticStatus Status { get; init; }

    /// <summary>Constat en une ligne.</summary>
    public required string Summary { get; init; }

    /// <summary>Explication : pourquoi c'est important, quoi faire si ce n'est pas corrigeable ici.</summary>
    public string Explanation { get; init; } = "";

    /// <summary>Détails mesurés (une ligne par élément : écran, barrette, GPU…).</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary>Le résultat est une estimation (ex. dual-channel).</summary>
    public bool IsEstimate { get; init; }

    public IReadOnlyList<DiagnosticFix> Fixes { get; init; } = [];

    /// <summary>Page d'OptiGame qui permet d'aller plus loin (ex. rechercher les mises à jour de pilotes).</summary>
    public DiagnosticLink? Link { get; init; }
}

public enum DiagnosticLinkTarget
{
    Drivers,
}

/// <summary>Lien vers une autre page d'OptiGame ; n'applique rien.</summary>
public sealed record DiagnosticLink(string Label, DiagnosticLinkTarget Target);

public interface IDiagnosticCheck
{
    string Id { get; }

    string Title { get; }

    DiagnosticResult Run();
}

public sealed class DiagnosticRunner(IEnumerable<IDiagnosticCheck> checks)
{
    public IReadOnlyList<IDiagnosticCheck> Checks { get; } = checks.ToList();

    public IReadOnlyList<DiagnosticResult> RunAll() => Checks.Select(Run).ToList();

    public static DiagnosticResult Run(IDiagnosticCheck check)
    {
        try
        {
            return check.Run();
        }
        catch (Exception ex)
        {
            return new DiagnosticResult
            {
                CheckId = check.Id,
                Title = check.Title,
                Status = DiagnosticStatus.Error,
                Summary = "Le contrôle n'a pas pu s'exécuter.",
                Explanation = ex.Message,
            };
        }
    }
}
