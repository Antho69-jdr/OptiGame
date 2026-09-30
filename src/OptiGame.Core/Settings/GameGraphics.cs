using OptiGame.Core.Changes;
using OptiGame.Core.Profiles;
using OptiGame.Core.State;

namespace OptiGame.Core.Settings;

public enum AutoHdrChoice
{
    /// <summary>Valeur laissée à Windows (y compris ses valeurs non documentées, ex. 2097).</summary>
    Windows,
    On,
    Off,
}

public enum GpuChoice
{
    Windows,
    PowerSaving,
    HighPerformance,
}

/// <summary>État lu dans UserGpuPreferences pour un jeu. <see cref="AutoHdrRaw"/> : valeur brute (ex. « 2097 »).</summary>
public sealed record GameGraphicsState(AutoHdrChoice AutoHdr, string? AutoHdrRaw, GpuChoice Gpu);

/// <summary>Une entrée de UserGpuPreferences qui désigne l'exe du jeu : valeur d'origine (avant OptiGame) et actuelle.</summary>
public sealed record GraphicsTarget(string ValueName, SettingValue Original, SettingValue Current);

/// <summary>
/// Auto HDR et carte graphique par jeu, dans HKCU\Software\Microsoft\DirectX\UserGpuPreferences (une valeur
/// « Clé=Valeur; » par chemin d'exe, lue par Windows au lancement du jeu : réglage durable, pas de session).
/// Vérifié sur la machine de dev le 2026-09-30 : Windows écrit de lui-même « AutoHDREnable=2097; » (valeur NON documentée)
/// et parfois « AppStatus=… », à conserver tels quels. OptiGame n'écrit que les valeurs documentées : AutoHDREnable 0 / 1,
/// GpuPreference 0 / 1 / 2. Journalisé dans fixes.json : annulable.
/// </summary>
public static class GameGraphics
{
    public const string AutoHdrKey = "AutoHDREnable";

    public static string ChangeId(Guid profileId) => $"game.graphics.{profileId:N}";

    public static GameGraphicsState Read(string? value)
    {
        var hdr = GpuPreferenceString.Get(value, AutoHdrKey);
        return new GameGraphicsState(
            hdr switch { "1" => AutoHdrChoice.On, "0" => AutoHdrChoice.Off, _ => AutoHdrChoice.Windows },
            hdr,
            GpuPreferenceString.Get(value, GpuPreferenceString.GpuPreferenceKey) switch
            {
                "1" => GpuChoice.PowerSaving,
                "2" => GpuChoice.HighPerformance,
                _ => GpuChoice.Windows,
            });
    }

    /// <summary>
    /// Nouvelle valeur, construite à partir de la valeur D'ORIGINE (celle d'avant OptiGame) : « Windows » garde la paire
    /// d'origine telle quelle, les autres paires (AppStatus…) sont conservées.
    /// </summary>
    public static string Build(string? original, AutoHdrChoice autoHdr, GpuChoice gpu)
    {
        var value = original ?? "";
        if (autoHdr != AutoHdrChoice.Windows) value = GpuPreferenceString.Set(value, AutoHdrKey, autoHdr == AutoHdrChoice.On ? "1" : "0");
        if (gpu != GpuChoice.Windows) value = GpuPreferenceString.Set(value, GpuPreferenceString.GpuPreferenceKey, gpu == GpuChoice.PowerSaving ? "1" : "2");
        return value;
    }

    /// <summary>
    /// Entrées à modifier : celles qui désignent déjà cet exe (chemins normalisés, ex. « Scrap Mechanic\.\Release\… »),
    /// plus le chemin normalisé s'il n'existe pas encore (c'est lui que Windows voit au lancement).
    /// </summary>
    public static IReadOnlyList<string> TargetNames(string exePath, IEnumerable<string> existingNames)
    {
        var normalized = ExePaths.Normalize(exePath);
        var names = existingNames
            .Where(n => !n.Equals(KnownSettings.GpuPreferencesGlobalValue, StringComparison.OrdinalIgnoreCase) && ExePaths.AreSame(n, normalized))
            .ToList();
        if (!names.Contains(normalized, StringComparer.OrdinalIgnoreCase)) names.Add(normalized);
        return names;
    }

    /// <summary>Changement à appliquer ; null si les deux choix sont « Windows » (il suffit alors d'annuler).</summary>
    public static ReversibleChange? Change(Guid profileId, string gameName, IReadOnlyList<GraphicsTarget> targets, AutoHdrChoice autoHdr, GpuChoice gpu)
    {
        if (autoHdr == AutoHdrChoice.Windows && gpu == GpuChoice.Windows) return null;

        var writes = targets
            .Select(t => new SettingWrite(KnownSettings.GpuPreference(t.ValueName), SettingValue.String(Build(t.Original.Text, autoHdr, gpu))))
            .ToList();
        var lines = targets.Select(t => $"{t.ValueName}\n  {Show(t.Current)} → {Build(t.Original.Text, autoHdr, gpu)}");
        var choices = new[]
        {
            autoHdr == AutoHdrChoice.Windows ? null : $"Auto HDR {(autoHdr == AutoHdrChoice.On ? "activé" : "désactivé")}",
            gpu switch { GpuChoice.PowerSaving => "carte graphique économe", GpuChoice.HighPerformance => "carte graphique la plus puissante", _ => null },
        }.OfType<string>();

        return new ReversibleChange
        {
            Id = ChangeId(profileId),
            Title = $"Graphismes de {gameName} : {string.Join(", ", choices)}",
            What = "Préférences graphiques de Windows pour ce jeu (HKCU\\…\\DirectX\\UserGpuPreferences) :\n" + string.Join("\n", lines),
            Why = "Réglages lus par Windows au lancement du jeu : pris en compte à la prochaine partie. L'Auto HDR convertit en HDR " +
                  "les jeux qui ne le gèrent pas (écran en HDR requis). Les autres paramètres de Windows pour ce jeu sont conservés.",
            Writes = writes,
        };
    }

    private static string Show(SettingValue value) => value.Kind == SettingValueKind.Absent ? "(aucun réglage)" : value.Text ?? "";
}
