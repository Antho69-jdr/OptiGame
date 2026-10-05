using OptiGame.Core.Abstractions;
using OptiGame.Core.Text;

namespace OptiGame.Core.Diagnostics.Checks;

public sealed class GpuDriverAgeCheck(IGpuInfoProvider gpus, TimeProvider time) : IDiagnosticCheck
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(183);

    public string Id => "gpu.driver-age";

    public string Title => "Pilote de la carte graphique";

    public DiagnosticResult Run()
    {
        var physical = gpus.GetAdapters().Where(g => g.IsPhysical).ToList();
        if (physical.Count == 0)
        {
            return Result(DiagnosticStatus.Info, "Aucune carte graphique physique détectée.", []);
        }

        var today = time.GetLocalNow().Date;
        var details = physical.Select(g =>
            $"{g.Name} : pilote {g.DriverVersion ?? "?"} du {(g.DriverDate is { } d ? FrenchText.Date(d) : "date inconnue")}").ToList();

        var old = physical.Where(g => g.DriverDate is { } d && today - d.Date > MaxAge).ToList();
        if (old.Count > 0)
        {
            var g = old[0];
            var months = (int)((today - g.DriverDate!.Value.Date).TotalDays / 30.4);
            return Result(DiagnosticStatus.NeedsAttention,
                $"Le pilote de {g.Name} date de {months} mois.",
                details);
        }

        if (physical.All(g => g.DriverDate is null))
        {
            return Result(DiagnosticStatus.Info, "Date du pilote inconnue.", details);
        }

        return Result(DiagnosticStatus.Ok, "Pilote graphique de moins de 6 mois.", details);
    }

    private DiagnosticResult Result(DiagnosticStatus status, string summary, IReadOnlyList<string> details) => new()
    {
        CheckId = Id,
        Title = Title,
        Status = status,
        Summary = summary,
        Details = details,
        Link = new DiagnosticLink("Rechercher les mises à jour de pilotes", DiagnosticLinkTarget.Drivers),
        Explanation =
            "Les pilotes récents apportent souvent des optimisations pour les jeux sortis récemment et des corrections " +
            "de bugs. La page « Pilotes » compare votre pilote au dernier publié par NVIDIA et liste ceux proposés par " +
            "Windows Update ; pour AMD et Intel, utilisez l'outil du fabricant (AMD Software: Adrenalin, Intel Graphics " +
            "Software). Un pilote plus ancien n'est pas un problème en soi si tout fonctionne bien.",
    };
}
