using System.Globalization;
using System.Text;
using OptiGame.Core.Text;

namespace OptiGame.Core.Drivers;

public enum ConfidenceLevel
{
    /// <summary>Problèmes ouverts non lus (PDF introuvable ou format changé) : on ne dit pas « sans risque ».</summary>
    Unknown,

    /// <summary>NVIDIA signale un problème encore ouvert dans un de vos jeux.</summary>
    Caution,

    /// <summary>Aucun problème ouvert ne cite vos jeux.</summary>
    NoKnownIssue,

    /// <summary>Aucun problème ouvert pour vos jeux, et le pilote corrige ou optimise au moins un de vos jeux.</summary>
    Recommended,
}

/// <summary>Avis sur une mise à jour de pilote, avec ses raisons (toutes vérifiables dans les notes de version).</summary>
public sealed record DriverConfidence(ConfidenceLevel Level, string Headline, IReadOnlyList<string> Reasons);

/// <summary>
/// « Note de confiance » d'un pilote NVIDIA : uniquement des faits publiés par NVIDIA, croisés avec les jeux de l'utilisateur
/// (problèmes ENCORE OUVERTS qui citent un de ses jeux, corrections et optimisations « Game Ready » pour ses jeux), plus l'âge
/// de la version. Pas de réputation tirée de forums : rien de fiable à lire automatiquement.
/// </summary>
public static class DriverConfidences
{
    /// <summary>Version publiée depuis moins de 3 jours : un correctif rapide reste possible.</summary>
    public const int RecentDays = 3;

    /// <param name="openIssues">Problèmes ouverts du PDF ; null = non lus.</param>
    /// <param name="games">Noms des jeux de l'utilisateur (Mes jeux).</param>
    public static DriverConfidence Evaluate(NvidiaDriver driver, IReadOnlyList<string>? openIssues, IReadOnlyList<string> games, DateOnly today)
    {
        var reasons = new List<string>();
        var concerned = openIssues is null ? [] : openIssues.Where(issue => games.Any(g => Mentions(issue, g))).ToList();
        var fixedForYou = (driver.FixedIssues ?? []).Where(fix => games.Any(g => Mentions(fix, g))).ToList();
        var readyForYou = driver.GameReadyTitle is { } title ? games.Where(g => Mentions(title, g)).ToList() : [];

        foreach (var issue in concerned) reasons.Add($"Problème encore ouvert signalé par NVIDIA : « {issue} ».");
        foreach (var fix in fixedForYou) reasons.Add($"Corrige : « {fix} ».");
        if (readyForYou.Count > 0) reasons.Add($"Optimisé (« Game Ready ») pour {FrenchText.Join(readyForYou)}.");

        if (openIssues is null)
        {
            reasons.Add("Les problèmes encore ouverts n'ont pas pu être lus dans les notes de version de NVIDIA.");
        }
        else if (concerned.Count == 0)
        {
            reasons.Add(openIssues.Count == 0
                ? "NVIDIA ne signale aucun problème encore ouvert dans cette version."
                : $"NVIDIA signale {FrenchText.Count(openIssues.Count, "problème encore ouvert", "problèmes encore ouverts")}, aucun dans vos jeux : " +
                  string.Join(" ; ", openIssues.Select(i => $"« {i} »")) + ".");
        }

        if (driver.ReleaseDate is { } released)
        {
            var days = today.DayNumber - released.DayNumber;
            reasons.Add(days < RecentDays
                ? $"Publiée {(days <= 0 ? "aujourd'hui" : days == 1 ? "hier" : $"il y a {days} jours")} : si rien ne presse, attendre quelques jours laisse le temps à un éventuel correctif."
                : $"Publiée il y a {days} jours.");
        }

        var (level, headline) = (openIssues, concerned.Count, fixedForYou.Count + readyForYou.Count) switch
        {
            (null, _, _) => (ConfidenceLevel.Unknown, "Problèmes connus non vérifiés"),
            (_, > 0, _) => (ConfidenceLevel.Caution, concerned.Count == 1 ? "Prudence : un problème connu touche un de vos jeux" : "Prudence : des problèmes connus touchent vos jeux"),
            (_, _, > 0) => (ConfidenceLevel.Recommended, "Conseillée : utile pour vos jeux, aucun problème connu"),
            _ => (ConfidenceLevel.NoKnownIssue, "Aucun problème connu pour vos jeux"),
        };
        return new DriverConfidence(level, headline, reasons);
    }

    /// <summary>
    /// Le texte cite-t-il ce jeu ? Nom entier, mots entiers, sans tenir compte de la casse, des accents, des ™ ® ni de la
    /// ponctuation (« PUBG: BATTLEGROUNDS » = « PUBG: Battlegrounds »). Noms de moins de 4 lettres ignorés (trop ambigus).
    /// </summary>
    public static bool Mentions(string text, string game)
    {
        var name = Normalize(game);
        return name.Replace(" ", "").Length >= 4 && $" {Normalize(text)} ".Contains($" {name} ", StringComparison.Ordinal);
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
