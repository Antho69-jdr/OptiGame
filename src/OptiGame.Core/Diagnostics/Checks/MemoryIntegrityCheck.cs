using OptiGame.Core.Abstractions;
using OptiGame.Core.Changes;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Diagnostics.Checks;

/// <summary>
/// Intégrité de la mémoire (HVCI) / VBS. Toujours « Info » : c'est un compromis sécurité/performance que seul
/// l'utilisateur peut trancher. Désactivation uniquement en option avancée.
/// </summary>
public sealed class MemoryIntegrityCheck(IDeviceGuardProvider deviceGuard, SettingAccessors settings) : IDiagnosticCheck
{
    public string Id => "security.memory-integrity";

    public string Title => "Intégrité de la mémoire (VBS / HVCI)";

    public DiagnosticResult Run()
    {
        var status = deviceGuard.GetStatus();
        if (status is null)
        {
            return Result("Informations indisponibles sur ce PC.", [], []);
        }

        var enabledValue = settings.Read(KnownSettings.HvciEnabled);
        var locked = settings.Read(KnownSettings.HvciLocked).AsDWord() == 1;
        var details = new List<string>
        {
            $"Sécurité basée sur la virtualisation (VBS) : {VbsText(status.VbsStatus)}",
            $"Intégrité de la mémoire (HVCI) : {(status.HvciRunning ? "active" : "inactive")}",
            $"HypervisorEnforcedCodeIntegrity\\Enabled (registre) = {enabledValue}",
        };
        if (locked)
        {
            details.Add("Verrouillée par le micrologiciel (UEFI) ou une stratégie.");
        }

        if (!status.HvciRunning)
        {
            var pending = enabledValue.AsDWord() == 1 ? " Activation en attente de redémarrage." : "";
            return Result("L'intégrité de la mémoire est désactivée." + pending, details, []);
        }

        if (enabledValue.AsDWord() == 0)
        {
            return Result("Active ; désactivation en attente de redémarrage.", details, []);
        }

        var fixes = locked ? [] : new[]
        {
            new DiagnosticFix(new ReversibleChange
            {
                Id = "fix.security.memory-integrity",
                Title = "Désactiver l'intégrité de la mémoire",
                What = $@"HKLM\...\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled : {enabledValue} → 0.",
                Why = "Peut améliorer les performances de quelques pourcents dans certains jeux, surtout sur des processeurs anciens.",
                Warning =
                    "Cela réduit la protection de Windows contre les pilotes malveillants et les attaques du noyau. " +
                    "Certains anti-cheats exigent qu'elle soit activée. Ne le faites que si vous avez mesuré un gain réel.",
                RequiresAdmin = true,
                RequiresReboot = true,
                Writes = [new SettingWrite(KnownSettings.HvciEnabled, SettingValue.DWord(0))],
            }, IsAdvanced: true),
        };
        return Result("L'intégrité de la mémoire est active.", details, fixes);
    }

    private static string VbsText(int status) => status switch
    {
        0 => "désactivée",
        1 => "activée mais inactive",
        2 => "active",
        _ => $"état {status}",
    };

    private DiagnosticResult Result(string summary, IReadOnlyList<string> details, IReadOnlyList<DiagnosticFix> fixes) => new()
    {
        CheckId = Id,
        Title = Title,
        Status = DiagnosticStatus.Info,
        Summary = summary,
        Details = details,
        Fixes = fixes,
        Explanation =
            "L'intégrité de la mémoire utilise la virtualisation pour empêcher du code malveillant de s'exécuter dans " +
            "le noyau de Windows (pilotes vulnérables, rootkits). Son coût en jeu va de négligeable à quelques " +
            "pourcents selon le jeu et le processeur : les processeurs récents disposent d'une accélération " +
            "matérielle (MBEC chez Intel, GMET chez AMD) qui le réduit fortement. Recommandation : laissez-la activée, " +
            "sauf si une mesure avant/après (page Mesures) montre un gain réel dans vos jeux.",
    };
}
