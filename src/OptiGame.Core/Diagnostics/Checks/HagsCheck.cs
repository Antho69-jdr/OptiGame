using OptiGame.Core.Abstractions;
using OptiGame.Core.Changes;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Diagnostics.Checks;

/// <summary>
/// Planification GPU à accélération matérielle. L'état réel vient du pilote (D3DKMT) : le registre HwSchMode
/// peut être absent (choix du pilote) et ne reflète l'état qu'après redémarrage.
/// </summary>
public sealed class HagsCheck(IGpuSchedulingProvider scheduling, SettingAccessors settings) : IDiagnosticCheck
{
    public string Id => "gpu.hags";

    public string Title => "Planification GPU à accélération matérielle (HAGS)";

    public DiagnosticResult Run()
    {
        var states = scheduling.GetStates();
        var registry = settings.Read(KnownSettings.HwSchMode);
        var details = states.Select(s =>
            $"{s.AdapterName} : {(s.Supported ? "prise en charge" : "non prise en charge")}, {(s.Enabled ? "activée" : "désactivée")}").ToList();
        details.Add($"HwSchMode (registre) = {registry}{(registry.IsAbsent ? " — choix du pilote" : "")}");

        var supported = states.Where(s => s.Supported).ToList();
        if (supported.Count == 0)
        {
            return Result(DiagnosticStatus.Info, "Non prise en charge par la carte graphique ou le pilote.", details, []);
        }

        var enabled = supported.Any(s => s.Enabled);
        var pending = (enabled, registry.AsDWord()) switch
        {
            (true, 1) => " Désactivation en attente de redémarrage.",
            (false, 2) => " Activation en attente de redémarrage.",
            _ => "",
        };

        if (enabled)
        {
            return Result(DiagnosticStatus.Ok, "Activée." + pending, details, []);
        }

        var fixes = registry.AsDWord() == 2 ? [] : new[]
        {
            new DiagnosticFix(new ReversibleChange
            {
                Id = "fix.gpu.hags",
                Title = "Activer HAGS",
                What = $@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode : {registry} → 2.",
                Why = "Requis pour la génération d'images DLSS (NVIDIA) ; peut réduire légèrement la latence selon le GPU et le jeu.",
                Warning = "L'effet dépend du GPU, du pilote et du jeu (parfois nul, rarement négatif). Redémarrage nécessaire.",
                RequiresAdmin = true,
                RequiresReboot = true,
                Writes = [new SettingWrite(KnownSettings.HwSchMode, SettingValue.DWord(2))],
            }),
        };
        return Result(DiagnosticStatus.Info, "Prise en charge mais désactivée." + pending, details, fixes);
    }

    private DiagnosticResult Result(DiagnosticStatus status, string summary, IReadOnlyList<string> details, IReadOnlyList<DiagnosticFix> fixes) => new()
    {
        CheckId = Id,
        Title = Title,
        Status = status,
        Summary = summary,
        Details = details,
        Fixes = fixes,
        Explanation =
            "HAGS laisse la carte graphique gérer elle-même sa file de travail au lieu de Windows. Le gain est en " +
            "général faible et dépend du GPU, du pilote et du jeu ; HAGS est en revanche nécessaire pour la " +
            "génération d'images DLSS (NVIDIA RTX 40 et plus récentes). Tout changement nécessite un redémarrage.",
    };
}
