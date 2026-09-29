using OptiGame.Core.Abstractions;
using OptiGame.Core.Changes;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Diagnostics.Checks;

/// <summary>
/// Préférence GPU par application, utile seulement sur les PC hybrides (GPU intégré + dédié).
/// </summary>
public sealed class GpuPreferenceCheck(IGpuInfoProvider gpus, IRegistryReader registry, SettingAccessors settings) : IDiagnosticCheck
{
    private const string HighPerformance = "2";

    public string Id => "gpu.preference";

    public string Title => "Préférence GPU par jeu (PC hybrides)";

    public DiagnosticResult Run()
    {
        var physical = gpus.GetAdapters().Where(g => g.IsPhysical).ToList();
        if (physical.Count < 2)
        {
            return Result(
                physical.Count == 1 ? $"Un seul GPU ({physical[0].Name}) : sans objet." : "Aucun GPU physique détecté : sans objet.",
                [], []);
        }

        var details = new List<string> { "GPU : " + string.Join(", ", physical.Select(g => g.Name)) };
        var fixes = new List<DiagnosticFix>();
        foreach (var exe in registry.GetValueNames(KnownSettings.GpuPreferencesKey))
        {
            if (exe.Equals(KnownSettings.GpuPreferencesGlobalValue, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = KnownSettings.GpuPreference(exe);
            var current = settings.Read(target);
            var preference = GpuPreferenceString.Get(current.Text, GpuPreferenceString.GpuPreferenceKey);
            details.Add($"{Path.GetFileName(exe)} : {PreferenceText(preference)}");

            if (preference != HighPerformance && current.Kind == SettingValueKind.String)
            {
                fixes.Add(new DiagnosticFix(new ReversibleChange
                {
                    Id = $"fix.gpu.preference.{exe}",
                    Title = $"GPU haute performance pour {Path.GetFileName(exe)}",
                    What = $"Préférence graphique de {exe} : {PreferenceText(preference)} → haute performance.",
                    Why = "Garantit que le jeu utilise le GPU dédié et non le GPU intégré, moins puissant.",
                    Writes =
                    [
                        new SettingWrite(target, SettingValue.String(
                            GpuPreferenceString.Set(current.Text, GpuPreferenceString.GpuPreferenceKey, HighPerformance))),
                    ],
                }));
            }
        }

        return Result(
            fixes.Count == 0
                ? "PC hybride : aucune application sans préférence « haute performance » parmi celles connues de Windows."
                : $"PC hybride : {fixes.Count} application(s) sans préférence « haute performance ».",
            details, fixes);
    }

    private static string PreferenceText(string? value) => value switch
    {
        null or "0" => "laissé à Windows",
        "1" => "économie d'énergie",
        "2" => "haute performance",
        _ => $"valeur {value}",
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
            "Sur un PC avec un GPU intégré et un GPU dédié (souvent un portable), Windows peut lancer un jeu sur le " +
            "GPU intégré, beaucoup moins puissant. La liste ci-dessus contient les applications déjà connues de " +
            "Windows (Paramètres > Affichage > Graphiques) ; tous ne sont pas forcément des jeux. Les profils de jeu " +
            "(phase 2) permettront de régler chaque jeu.",
    };
}
