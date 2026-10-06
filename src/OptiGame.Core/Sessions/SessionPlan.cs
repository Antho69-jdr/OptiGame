using OptiGame.Core.Abstractions;
using OptiGame.Core.Changes;
using OptiGame.Core.Profiles;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Sessions;

/// <summary>Traduit un profil en changements de session, et en texte lisible pour l'utilisateur.</summary>
public static class SessionPlan
{
    public const string PowerChangeId = "session.power-plan";

    public static string CloseChangeId(string exeName) => $"session.close.{exeName.ToLowerInvariant()}";

    public static string PriorityLabel(GamePriority priority) => priority switch
    {
        GamePriority.AboveNormal => "Supérieure à la normale",
        GamePriority.High => "Haute",
        _ => "Normale",
    };

    public static ReversibleChange? PowerChange(GameProfile profile, IReadOnlyList<PowerScheme> schemes)
    {
        if (profile.PowerSchemeId is not { } id)
        {
            return null;
        }

        var name = SchemeName(id, schemes);
        return new ReversibleChange
        {
            Id = PowerChangeId,
            Title = $"Plan d'alimentation « {name} »",
            What = $"Plan d'alimentation actif → « {name} » pendant la session.",
            Why = "Choisi dans le profil du jeu.",
            Writes = [new SettingWrite(KnownSettings.ActivePowerScheme, SettingValue.String(id.ToString()))],
        };
    }

    public const string VisualEffectsChangeId = "session.visual-effects";

    /// <summary>
    /// Effets visuels de Windows coupés pendant la partie (animations, transparence), rétablis à la fin. Confort, pas de FPS :
    /// Windows n'anime rien pendant qu'un jeu est au premier plan (dit tel quel à l'utilisateur, principe « pas de placebo »).
    /// </summary>
    public static ReversibleChange? VisualEffectsChange(GameProfile profile) => !profile.ReduceVisualEffects ? null : new ReversibleChange
    {
        Id = VisualEffectsChangeId,
        Title = "Effets visuels de Windows coupés",
        What = "Animations des fenêtres et des éléments, et transparence de Windows, coupées pendant la session.",
        Why = "Choisi dans le profil du jeu : rien ne s'anime ni ne se superpose à la sortie du jeu (Alt+Tab, superpositions). " +
              "Confort seulement : pas de FPS en plus.",
        Writes =
        [
            new SettingWrite(KnownSettings.ClientAreaAnimation, SettingValue.DWord(0)),
            new SettingWrite(KnownSettings.WindowAnimation, SettingValue.DWord(0)),
            new SettingWrite(KnownSettings.Transparency, SettingValue.DWord(0)),
        ],
    };

    /// <summary>Fermeture journalisée : la restauration relance le programme (sans droits administrateur).</summary>
    public static ReversibleChange CloseAndRelaunch(ProcessToClose process) => new()
    {
        Id = CloseChangeId(process.ExeName),
        Title = $"Fermer {process.ExeName}",
        What = $"{process.ExeName} est fermé pendant la session puis relancé.",
        Why = "Choisi dans le profil du jeu.",
        Writes = [new SettingWrite(KnownSettings.RunningProcess(process.ExeName), SettingValue.Absent)],
    };

    public static IReadOnlyList<string> Describe(GameProfile profile, IReadOnlyList<PowerScheme> schemes)
    {
        var lines = new List<string>
        {
            profile.PowerSchemeId is { } id
                ? $"Plan d'alimentation → « {SchemeName(id, schemes)} »."
                : "Plan d'alimentation : inchangé.",
        };

        foreach (var process in profile.ProcessesToClose)
        {
            lines.Add(process.Relaunch
                ? $"Fermer {process.ExeName}, puis le relancer à la fin de la session s'il était ouvert."
                : $"Fermer {process.ExeName} (pas de relance).");
        }

        lines.Add(profile.Priority == GamePriority.Normal
            ? "Priorité du jeu : inchangée (normale)."
            : $"Priorité du jeu : {PriorityLabel(profile.Priority)}.");

        if (profile.ReduceVisualEffects) lines.Add("Animations et transparence de Windows coupées (confort, pas de FPS en plus).");

        return lines;
    }

    private static string SchemeName(Guid id, IReadOnlyList<PowerScheme> schemes) =>
        schemes.FirstOrDefault(s => s.Id == id)?.Name ?? id.ToString();
}
