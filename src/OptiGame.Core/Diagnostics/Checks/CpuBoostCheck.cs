using OptiGame.Core.Abstractions;
using OptiGame.Core.Changes;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Diagnostics.Checks;

/// <summary>
/// Boost du processeur dans le plan d'alimentation actif (sur secteur). « Désactivé » ou un état maximal sous 100 % (astuce
/// courante pour couper le turbo) bloquent le processeur à sa fréquence de base : 10 à 15 % de FPS en moins dans les jeux
/// limités par le processeur. La correction remet les valeurs par défaut de Windows (boost « Offensif », 100 %), relevées sur
/// les plans Utilisation normale et Haute performance de la machine de dev.
/// </summary>
public sealed class CpuBoostCheck(IPowerSchemeProvider power) : IDiagnosticCheck
{
    public const uint BoostDisabled = 0;
    public const uint BoostAggressive = 2;

    public string Id => "power.boost";

    public string Title => "Boost du processeur";

    public DiagnosticResult Run()
    {
        var active = power.GetActiveScheme();
        var name = power.GetSchemes().FirstOrDefault(s => s.Id == active)?.Name ?? active.ToString();
        var boost = power.ReadAcValue(active, PowerSettings.SubProcessor, PowerSettings.PerfBoostMode);
        var max = power.ReadAcValue(active, PowerSettings.SubProcessor, PowerSettings.ProcThrottleMax);
        var details = new List<string>
        {
            $"Plan actif : {name}.",
            $"Mode d'amélioration des performances (boost) : {BoostLabel(boost)}.",
            $"État maximal du processeur : {(max is { } m ? $"{m} %" : "non défini par le plan")}.",
        };

        var fixes = new List<DiagnosticFix>();
        var problems = new List<string>();
        if (boost == BoostDisabled)
        {
            problems.Add("le boost est désactivé");
            fixes.Add(Fix("fix.power.boost", active, PowerSettings.PerfBoostMode, BoostAggressive,
                "Réactiver le boost du processeur",
                $"Plan « {name} », sur secteur : mode d'amélioration des performances « Désactivé » → « Offensif » (valeur par défaut de Windows)."));
        }
        if (max is < 100)
        {
            problems.Add($"l'état maximal est limité à {max} %");
            fixes.Add(Fix("fix.power.maxstate", active, PowerSettings.ProcThrottleMax, 100,
                "Remettre l'état maximal du processeur à 100 %",
                $"Plan « {name} », sur secteur : état maximal du processeur {max} % → 100 % (valeur par défaut de Windows)."));
        }

        if (problems.Count > 0)
        {
            return Result(DiagnosticStatus.NeedsAttention, $"Processeur bridé : {string.Join(" et ", problems)}.",
                "Le processeur reste à sa fréquence de base au lieu de monter en fréquence quand un jeu le sollicite : jusqu'à 10 à 15 % " +
                "de FPS en moins dans les jeux limités par le processeur. Ce réglage est souvent modifié par des outils d'économie d'énergie " +
                "ou de réduction de la température. Sur un portable, seul le fonctionnement sur secteur est modifié.",
                details, fixes);
        }
        if (boost is null)
        {
            return Result(DiagnosticStatus.Info, "Boost non défini par le plan actif.",
                "Ce plan ne précise pas le boost : c'est alors le comportement par défaut de Windows et du processeur qui s'applique.",
                details, []);
        }
        return Result(DiagnosticStatus.Ok, $"Boost actif ({BoostLabel(boost)}).",
            "Le processeur peut dépasser sa fréquence de base quand un jeu le sollicite.", details, []);
    }

    public static string BoostLabel(uint? value) => value switch
    {
        null => "non défini par le plan",
        0 => "Désactivé",
        1 => "Activé",
        2 => "Offensif",
        3 => "Activé en mode efficace",
        4 => "Offensif en mode efficace",
        5 => "Offensif garanti",
        6 => "Offensif efficace garanti",
        _ => $"valeur {value}",
    };

    private static DiagnosticFix Fix(string id, Guid scheme, Guid setting, uint value, string title, string what) => new(new ReversibleChange
    {
        Id = id,
        Title = title,
        What = what,
        Why = "Le processeur pourra de nouveau monter en fréquence pendant les jeux.",
        RequiresAdmin = true,
        Writes = [new SettingWrite(KnownSettings.PowerSetting(scheme, PowerSettings.SubProcessor, setting), SettingValue.DWord(value))],
    });

    private DiagnosticResult Result(DiagnosticStatus status, string summary, string explanation, IReadOnlyList<string> details,
        IReadOnlyList<DiagnosticFix> fixes) => new()
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
