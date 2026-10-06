using OptiGame.Core.Changes;
using OptiGame.Core.Rating;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.InGame;

/// <summary>
/// Qualité graphique d'un jeu Unreal Engine réglée par OptiGame : tous les groupes de qualité trouvés dans son
/// GameUserSettings.ini (section [ScalabilityGroups], hors échelle de rendu et paysage) mis au même niveau, comme le fait le
/// réglage général du jeu. Journal des corrections durables (fixes.json, id « game.unreal-quality.&lt;profil&gt; ») : les valeurs
/// d'origine sont restaurées clé par clé. À faire jeu FERMÉ : un jeu ouvert réécrit ce fichier en quittant.
/// </summary>
public static class UnrealQuality
{
    public const string Section = "ScalabilityGroups";

    public static string ChangeId(Guid profileId) => $"game.unreal-quality.{profileId:N}";

    /// <summary>
    /// Niveau du jeu pour un réglage d'OptiGame : le plus haut des niveaux « Bas » (PUBG : « Bas » plutôt que « Très bas »), le
    /// premier des niveaux « Ultra » (moteur : « Épique » plutôt que « Cinématique »).
    /// </summary>
    public static int LevelFor(UnrealSettings.QualityScale scale, GraphicsPreset preset)
    {
        var matching = Enumerable.Range(0, scale.Levels.Count).Where(i => scale.Levels[i].Preset == preset).ToList();
        if (matching.Count == 0) return preset <= GraphicsPreset.Low ? 0 : scale.Levels.Count - 1;
        return preset == GraphicsPreset.Ultra ? matching[0] : matching[^1];
    }

    /// <summary>« « Moyen » (niveau 2) ».</summary>
    public static string LevelLabel(UnrealSettings.QualityScale scale, int level) => $"« {scale.Levels[level].Name} » (niveau {level})";

    public static ReversibleChange Change(Guid profileId, string gameName, InGameSettings settings, int level)
    {
        if (settings.Scale is not { } scale || settings.QualityKeys is not { Count: > 0 } keys)
        {
            throw new ArgumentException("Ce jeu n'a pas de groupes de qualité lisibles.", nameof(settings));
        }
        var current = settings.QualityLevel is { } from ? $"{LevelLabel(scale, from)} → " : "";
        return new ReversibleChange
        {
            Id = ChangeId(profileId),
            Title = $"{gameName} : qualité graphique « {scale.Levels[level].Name} »",
            What = $"Fichier {UnrealSettings.FileName} du jeu, {keys.Count} groupes de qualité ({string.Join(", ", keys)}) : " +
                   $"{current}{LevelLabel(scale, level)}.",
            Why = "Comme le réglage général dans les options du jeu, pris en compte au prochain lancement. Si vous changez ensuite la " +
                  "qualité dans le jeu, « Restaurer l'original » remettra les valeurs d'avant OptiGame.",
            Writes = [.. keys.Select(key => new SettingWrite(KnownSettings.IniValue(settings.SourcePath, Section, key), SettingValue.String(level.ToString(System.Globalization.CultureInfo.InvariantCulture))))],
        };
    }
}
