using OptiGame.Core.Abstractions;

namespace OptiGame.Core.Diagnostics.Checks;

public sealed class PowerSourceCheck(IPowerStatusProvider power) : IDiagnosticCheck
{
    public string Id => "power.source";

    public string Title => "Alimentation et économiseur d'énergie";

    public DiagnosticResult Run()
    {
        var s = power.GetStatus();
        var details = new List<string>
        {
            s.HasBattery ? $"Batterie : {s.BatteryPercent?.ToString() ?? "?"} %" : "Pas de batterie (PC de bureau)",
            s.OnAcPower switch { true => "Sur secteur", false => "Sur batterie", null => "Source d'alimentation inconnue" },
            s.EnergySaverOn ? "Économiseur d'énergie : activé" : "Économiseur d'énergie : désactivé",
        };

        if (s.HasBattery && s.OnAcPower == false)
        {
            return Result(DiagnosticStatus.NeedsAttention, "Le portable fonctionne sur batterie.",
                "Sur batterie, Windows et le pilote graphique réduisent fortement les performances (fréquences CPU/GPU " +
                "limitées). Branchez le chargeur pour jouer.", details);
        }

        if (s.EnergySaverOn)
        {
            return Result(DiagnosticStatus.NeedsAttention, "L'économiseur d'énergie est activé.",
                "L'économiseur d'énergie réduit les performances pour préserver l'autonomie. Désactivez-le dans " +
                "Paramètres > Système > Alimentation (ou depuis le centre de notifications). OptiGame ne le modifie pas " +
                "lui-même : Windows ne propose pas d'interface documentée pour cela.", details);
        }

        return Result(DiagnosticStatus.Ok,
            s.HasBattery ? "Sur secteur, économiseur d'énergie désactivé." : "PC de bureau, économiseur d'énergie désactivé.",
            "", details);
    }

    private DiagnosticResult Result(DiagnosticStatus status, string summary, string explanation, IReadOnlyList<string> details) => new()
    {
        CheckId = Id,
        Title = Title,
        Status = status,
        Summary = summary,
        Explanation = explanation,
        Details = details,
    };
}
