using OptiGame.Core.Changes;
using OptiGame.Core.Profiles;
using OptiGame.Core.State;

namespace OptiGame.Core.Settings;

public enum GpuChoice
{
    Windows,
    PowerSaving,
    HighPerformance,
}

/// <summary>État lu dans UserGpuPreferences pour un jeu. <see cref="AutoHdrRaw"/> : valeur brute de Windows (ex. « 2097 »).</summary>
public sealed record GameGraphicsState(string? AutoHdrRaw, GpuChoice Gpu);

/// <summary>Une entrée de UserGpuPreferences qui désigne l'exe du jeu : valeur d'origine (avant OptiGame) et actuelle.</summary>
public sealed record GraphicsTarget(string ValueName, SettingValue Original, SettingValue Current);

/// <summary>
/// Préférences graphiques de Windows pour un jeu, dans HKCU\Software\Microsoft\DirectX\UserGpuPreferences (une valeur
/// « Clé=Valeur; » par chemin d'exe, lue au lancement du jeu : réglage durable, pas de session). Journalisé dans fixes.json.
/// <para>
/// Carte graphique : GpuPreference 0 / 1 / 2 (valeurs documentées par Microsoft) : écrite par OptiGame.
/// </para>
/// <para>
/// Auto HDR : LU SEULEMENT. Expérience sur la machine de dev (2026-09-30, Paramètres > Affichage > Graphiques) : Windows
/// écrit par défaut « AutoHDREnable=2097; », puis « 6193 » après que l'utilisateur a désactivé PUIS réactivé l'Auto HDR
/// d'un jeu ; même valeur dans les deux états, et aucune autre clé modifiée. Encodage non documenté et non élucidé :
/// OptiGame n'écrit jamais AutoHDREnable (les valeurs 0/1 trouvées en ligne ne correspondent pas à ce que fait Windows)
/// et renvoie vers les réglages de Windows. Les paires de Windows (AutoHDREnable, AppStatus, SwapEffectUpgradeEnable…)
/// sont conservées telles quelles.
/// </para>
/// </summary>
public static class GameGraphics
{
    public const string AutoHdrKey = "AutoHDREnable";

    public static string ChangeId(Guid profileId) => $"game.graphics.{profileId:N}";

    public static GameGraphicsState Read(string? value) => new(
        GpuPreferenceString.Get(value, AutoHdrKey),
        GpuPreferenceString.Get(value, GpuPreferenceString.GpuPreferenceKey) switch
        {
            "1" => GpuChoice.PowerSaving,
            "2" => GpuChoice.HighPerformance,
            _ => GpuChoice.Windows,
        });

    /// <summary>
    /// Nouvelle valeur, construite à partir de la valeur D'ORIGINE (celle d'avant OptiGame) : seule la paire GpuPreference
    /// est posée ; toutes les autres (dont AutoHDREnable) sont conservées.
    /// </summary>
    public static string Build(string? original, GpuChoice gpu) =>
        gpu == GpuChoice.Windows
            ? original ?? ""
            : GpuPreferenceString.Set(original, GpuPreferenceString.GpuPreferenceKey, gpu == GpuChoice.PowerSaving ? "1" : "2");

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

    /// <summary>Changement à appliquer ; null pour « Laisser Windows décider » (il suffit alors d'annuler).</summary>
    public static ReversibleChange? Change(Guid profileId, string gameName, IReadOnlyList<GraphicsTarget> targets, GpuChoice gpu)
    {
        if (gpu == GpuChoice.Windows) return null;

        var label = gpu == GpuChoice.PowerSaving ? "carte graphique économe" : "carte graphique la plus puissante";
        return new ReversibleChange
        {
            Id = ChangeId(profileId),
            Title = $"Graphismes de {gameName} : {label}",
            What = "Préférences graphiques de Windows pour ce jeu (HKCU\\…\\DirectX\\UserGpuPreferences) :\n" +
                   string.Join("\n", targets.Select(t => $"{t.ValueName}\n  {Show(t.Current)} → {Build(t.Original.Text, gpu)}")),
            Why = "Sur un PC à deux cartes graphiques, Windows peut lancer un jeu sur la moins puissante. Réglage lu par Windows " +
                  "au lancement du jeu : pris en compte à la prochaine partie. Les autres paramètres de Windows pour ce jeu " +
                  "(dont l'Auto HDR) sont conservés.",
            Writes = targets
                .Select(t => new SettingWrite(KnownSettings.GpuPreference(t.ValueName), SettingValue.String(Build(t.Original.Text, gpu))))
                .ToList(),
        };
    }

    private static string Show(SettingValue value) => value.Kind == SettingValueKind.Absent ? "(aucun réglage)" : value.Text ?? "";
}
