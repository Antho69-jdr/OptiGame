using OptiGame.Core.Changes;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Gpu;

/// <summary>
/// Modèle DLSS le plus récent imposé par le pilote NVIDIA pour un jeu, dans le
/// profil du pilote du jeu : les fichiers du jeu ne sont PAS modifiés (remplacer nvngx_dlss.dll dans le dossier du jeu
/// nvngx_dlss.dll et peut être signalé par un anti-triche). Réglages officiels (NvApiDriverSettings.h, lu le 2026-10-07, présents
/// dans le pilote 617.42 de la machine de dev) : NGX_DLSS_SR_OVERRIDE 0x10E41E01 (1 = activé) et
/// NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION 0x10E41DF3 (0x00FFFFFF = RENDER_PRESET_Latest). Effet seulement si le DLSS est
/// activé dans le jeu.
/// </summary>
public static class DlssOverride
{
    public const uint OverrideId = 0x10E41E01;
    public const uint PresetId = 0x10E41DF3;
    public const uint On = 1;
    public const uint LatestPreset = 0x00FFFFFF;

    public static string ChangeId(Guid profileId) => $"game.dlss-latest.{profileId:N}";

    /// <summary>« modèle le plus récent », « préréglage K », « celui du jeu, rien d'imposé ».</summary>
    public static string Describe(uint? overrideValue, uint? preset) =>
        overrideValue != On || preset is null or 0 ? "celui du jeu, rien d'imposé"
        : preset == LatestPreset ? "modèle le plus récent"
        : preset <= 15 ? $"préréglage {(char)('A' + preset.Value - 1)}"
        : $"valeur 0x{preset:X}";

    /// <param name="driverProfile">Profil que le pilote applique au jeu ; null = aucun, OptiGame en crée un.</param>
    public static ReversibleChange Change(Guid profileId, string gameName, string exePath, string? driverProfile, string currentText)
    {
        var profile = driverProfile is null
            ? $"nouveau profil « OptiGame - {Path.GetFileName(exePath)} » (supprimé si l'on annule)"
            : $"profil « {driverProfile} »";
        return new ReversibleChange
        {
            Id = ChangeId(profileId),
            Title = $"{gameName} : modèle DLSS le plus récent (pilote NVIDIA)",
            What = $"Pilote NVIDIA, {profile} : remplacement du DLSS (« Enable DLSS-SR override ») activé, préréglage → le plus récent " +
                   $"(actuellement : {currentText}).",
            Why = "Le pilote utilise son modèle DLSS le plus récent (meilleure netteté et stabilité de l'image, parfois un peu plus " +
                  "exigeant) au lieu de celui livré avec le jeu, sans modifier les fichiers du jeu. N'agit que si le DLSS est activé " +
                  "dans le jeu ; pris en compte au prochain lancement.",
            Writes =
            [
                new SettingWrite(KnownSettings.NvidiaSetting(exePath, OverrideId), SettingValue.DWord(On)),
                new SettingWrite(KnownSettings.NvidiaSetting(exePath, PresetId), SettingValue.DWord(LatestPreset)),
            ],
        };
    }

    /// <summary>
    /// Dossier où chercher la bibliothèque DLSS du jeu : le dossier du jeu dans steamapps\common, sinon la racine d'un jeu Unreal
    /// (au-dessus de &lt;projet&gt;\Binaries\Win64), sinon le dossier de l'exe. Relevé le 2026-10-07 : Overwatch et Void Crew à
    /// côté de l'exe, Star Citizen dans Bin64, ARC Raiders et PUBG dans Engine\Plugins\…\Win64 (jusqu'à 9 niveaux).
    /// </summary>
    public static string SearchRoot(string exePath)
    {
        var parts = exePath.Split('\\', '/');
        var common = Array.FindIndex(parts, p => p.Equals("common", StringComparison.OrdinalIgnoreCase));
        if (common > 0 && parts[common - 1].Equals("steamapps", StringComparison.OrdinalIgnoreCase) && common + 2 < parts.Length)
        {
            return string.Join('\\', parts[..(common + 2)]);
        }
        var directory = Path.GetDirectoryName(exePath) ?? exePath;
        if (Path.GetFileName(directory).Equals("Win64", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(Path.GetDirectoryName(directory) ?? "").Equals("Binaries", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(directory))) ?? directory;
        }
        return directory;
    }
}
