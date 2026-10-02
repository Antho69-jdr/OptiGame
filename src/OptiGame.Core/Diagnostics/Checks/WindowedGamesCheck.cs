using OptiGame.Core.Changes;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Diagnostics.Checks;

/// <summary>
/// « Optimisations pour les jeux en mode fenêtré » (Paramètres > Système > Écran > Graphiques) : les jeux DirectX 10 et 11 en
/// fenêtré ou plein écran fenêtré passent au modèle de présentation « flip » (moins de latence ; nécessaire à l'Auto HDR et au
/// taux de rafraîchissement variable). Valeur SwapEffectUpgradeEnable de DirectXUserGlobalSettings : 1 = activé (machine de dev),
/// 0 = désactivé (posé par certains scripts « gaming »). Absente : état par défaut de Windows, NON documenté par Microsoft
/// (support.microsoft.com, article 3f006843, consulté le 2026-10-03) → information, avec une activation explicite proposée.
/// Activer l'Auto HDR active aussi ce réglage.
/// </summary>
public sealed class WindowedGamesCheck(SettingAccessors settings) : IDiagnosticCheck
{
    public const string Key = "SwapEffectUpgradeEnable";

    public string Id => "windows.windowed-games";

    public string Title => "Optimisations pour les jeux fenêtrés";

    public DiagnosticResult Run()
    {
        var current = settings.Read(KnownSettings.DirectXGlobalSettings);
        var text = current.Text;
        var value = GpuPreferenceString.Get(text, Key);
        var details = new List<string> { $"DirectXUserGlobalSettings = {(current.IsAbsent ? "(absente)" : $"« {text} »")}" };

        if (value == "1")
        {
            return Result(DiagnosticStatus.Ok, "Activées.",
                "Les jeux DirectX 10 et 11 en fenêtré ou plein écran fenêtré utilisent le modèle de présentation moderne.", details, []);
        }

        var fix = new DiagnosticFix(new ReversibleChange
        {
            Id = "fix.windows.windowed-games",
            Title = "Activer les optimisations pour les jeux fenêtrés",
            What = $@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings : {Key}={value ?? "(absent)"} → {Key}=1 " +
                   "(les autres réglages de cette valeur sont conservés).",
            Why = "Moins de latence et une meilleure fluidité dans les jeux DirectX 10 et 11 en fenêtré ou plein écran fenêtré ; " +
                  "nécessaire à l'Auto HDR et au taux de rafraîchissement variable dans ces modes.",
            Writes = [new SettingWrite(KnownSettings.DirectXGlobalSettings, SettingValue.String(GpuPreferenceString.Set(text, Key, "1")))],
        });

        if (value == "0")
        {
            return Result(DiagnosticStatus.NeedsAttention, "Désactivées.",
                "Les jeux DirectX 10 et 11 en fenêtré ou plein écran fenêtré gardent l'ancien modèle de présentation : plus de latence, " +
                "et pas d'Auto HDR ni de taux de rafraîchissement variable dans ces modes. Ce réglage est souvent désactivé par des " +
                "scripts d'optimisation.", details, [fix]);
        }

        return Result(DiagnosticStatus.Info, "Réglage par défaut de Windows.",
            "Windows n'a jamais enregistré ce choix : c'est son comportement par défaut qui s'applique, que Microsoft ne documente pas. " +
            "L'activation explicite garantit le modèle de présentation moderne aux jeux DirectX 10 et 11 en fenêtré.", details, [fix]);
    }

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
