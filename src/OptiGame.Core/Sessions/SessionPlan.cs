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

        return lines;
    }

    private static string SchemeName(Guid id, IReadOnlyList<PowerScheme> schemes) =>
        schemes.FirstOrDefault(s => s.Id == id)?.Name ?? id.ToString();
}
