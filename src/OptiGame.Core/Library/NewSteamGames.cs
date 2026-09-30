namespace OptiGame.Core.Library;

/// <summary>Jeu Steam installé, vu dans son manifeste (appmanifest_*.acf).</summary>
public sealed record SteamInstall(string AppId, int? StateFlags);

/// <summary>
/// Nouveaux jeux Steam à proposer dans « Mes jeux ». StateFlags du manifeste : bit 4 = entièrement installé (valeur 4 pour
/// tous les jeux installés de la machine de dev, 2026-09-30) ; pendant un téléchargement, Steam utilise d'autres valeurs
/// (ex. 1026 = mise à jour en cours). Au tout premier passage, les jeux déjà installés sont seulement mémorisés : seules
/// les installations suivantes sont proposées.
/// </summary>
public static class NewSteamGames
{
    public const int FullyInstalled = 4;

    public static bool IsFullyInstalled(int? stateFlags) => stateFlags is { } flags && (flags & FullyInstalled) != 0;

    /// <summary>À proposer : entièrement installés, jamais vus (ni proposés, ni ignorés) et sans profil.</summary>
    public static IReadOnlyList<string> ToPropose(IEnumerable<SteamInstall> installed, IReadOnlyCollection<string> known, IReadOnlyCollection<string> profileAppIds) =>
        installed
            .Where(i => IsFullyInstalled(i.StateFlags) && !known.Contains(i.AppId) && !profileAppIds.Contains(i.AppId))
            .Select(i => i.AppId)
            .Distinct()
            .ToList();
}
