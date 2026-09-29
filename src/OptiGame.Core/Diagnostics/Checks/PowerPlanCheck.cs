using OptiGame.Core.Abstractions;
using OptiGame.Core.Changes;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Diagnostics.Checks;

public sealed class PowerPlanCheck(IPowerSchemeProvider power) : IDiagnosticCheck
{
    public string Id => "power.plan";

    public string Title => "Plan d'alimentation";

    public DiagnosticResult Run()
    {
        var active = power.GetActiveScheme();
        var schemes = power.GetSchemes();
        var activeName = schemes.FirstOrDefault(s => s.Id == active)?.Name ?? active.ToString();
        var details = schemes.Select(s => $"{s.Name}{(s.Id == active ? " (actif)" : "")}").ToList();

        if (active == PowerSchemes.HighPerformance || active == PowerSchemes.UltimatePerformance)
        {
            return Result(DiagnosticStatus.Ok, $"Plan actif : {activeName}.",
                "Plan orienté performance : le processeur monte en fréquence sans délai.", details, []);
        }

        if (active == PowerSchemes.Balanced)
        {
            return Result(DiagnosticStatus.Info, $"Plan actif : {activeName}.",
                "Le plan Équilibré convient très bien au jeu sur la plupart des PC récents : l'écart avec Performances " +
                "élevées est généralement faible. Les profils de jeu (phase 2) pourront activer un autre plan " +
                "uniquement pendant vos parties. Sur un portable, le curseur « Mode d'alimentation » des paramètres " +
                "Windows compte aussi.",
                details, []);
        }

        if (active == PowerSchemes.PowerSaver)
        {
            var target = schemes.FirstOrDefault(s => s.Id == PowerSchemes.HighPerformance)
                ?? schemes.FirstOrDefault(s => s.Id == PowerSchemes.Balanced);
            var fixes = target is null ? [] : new[]
            {
                new DiagnosticFix(new ReversibleChange
                {
                    Id = "fix.power.plan",
                    Title = $"Activer le plan « {target.Name} »",
                    What = $"Plan d'alimentation actif : « {activeName} » → « {target.Name} ».",
                    Why = "Le plan Économie d'énergie limite la fréquence du processeur, ce qui réduit fortement les FPS.",
                    Writes = [new SettingWrite(KnownSettings.ActivePowerScheme, SettingValue.String(target.Id.ToString()))],
                }),
            };
            return Result(DiagnosticStatus.NeedsAttention, $"Plan actif : {activeName}.",
                "Le plan Économie d'énergie bride le processeur : à éviter pour jouer.", details, fixes);
        }

        return Result(DiagnosticStatus.Info, $"Plan personnalisé actif : {activeName}.",
            "Ce plan a été créé par un outil ou un fabricant (ex. AtlasOS, pilote chipset). OptiGame ne peut pas juger " +
            "de ses réglages ; s'il a été installé par un outil d'optimisation, il est probablement déjà orienté performance.",
            details, []);
    }

    private DiagnosticResult Result(DiagnosticStatus status, string summary, string explanation, IReadOnlyList<string> details, IReadOnlyList<DiagnosticFix> fixes) => new()
    {
        CheckId = Id,
        Title = Title,
        Status = status,
        Summary = summary,
        Explanation = explanation,
        Details = details,
        Fixes = fixes,
    };
}
