using OptiGame.Core.State;

namespace OptiGame.Core.Profiles;

/// <summary>
/// Réglages durables faits pour un jeu depuis sa page (carte graphique, plafond de FPS du pilote…) : journal des corrections
/// (fixes.json), ids « game.&lt;sorte&gt;.&lt;id du profil&gt; ». Retirer le jeu de « Mes jeux » ne les annule pas : ils restent
/// annulables depuis le Diagnostic (vue Avancé, « Corrections appliquées par OptiGame »).
/// </summary>
public static class GameChanges
{
    /// <summary>Changements actifs propres à ce profil, dans l'ordre d'application.</summary>
    public static IReadOnlyList<ChangeRecord> Of(IEnumerable<ChangeRecord> active, Guid profileId)
    {
        var suffix = $".{profileId:N}";
        return active
            .Where(c => c.Id.StartsWith("game.", StringComparison.Ordinal) && c.Id.EndsWith(suffix, StringComparison.Ordinal))
            .ToList();
    }
}
