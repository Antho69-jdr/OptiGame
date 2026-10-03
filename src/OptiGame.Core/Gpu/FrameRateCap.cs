using OptiGame.Core.Changes;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Gpu;

/// <summary>
/// Plafond de FPS par jeu dans les profils du pilote NVIDIA (réglage FRL_FPS = 0x10835002, « Frame Rate Limiter » de
/// NvApiDriverSettings.h : 0 = désactivé, 1023 au plus). Conseil : fréquence de l'écran − 3, pour rester dans la plage du taux de
/// rafraîchissement variable (G-Sync / FreeSync) sans tomber dans la latence de la V-Sync. Un limiteur intégré au jeu ou NVIDIA
/// Reflex reste préférable (latence plus faible) : c'est dit à l'utilisateur.
/// </summary>
public static class FrameRateCap
{
    public const uint SettingId = 0x10835002;
    public const int Minimum = 20;
    public const int Maximum = 1000;

    public static string ChangeId(Guid profileId) => $"game.framecap.{profileId:N}";

    public static int Suggested(int refreshHz) => Math.Max(Minimum, refreshHz - 3);

    public static string Describe(uint? value) => value is null or 0 ? "aucun plafond" : $"{value} FPS";

    /// <summary>Message d'erreur si la saisie n'est pas un plafond valide (0 = aucun plafond), sinon null.</summary>
    public static string? Validate(int? fps) =>
        fps is null ? "Indiquez un nombre d'images par seconde."
        : fps == 0 || fps is >= Minimum and <= Maximum ? null
        : $"Plafond entre {Minimum} et {Maximum} FPS (ou 0 pour aucun plafond).";

    /// <param name="driverProfile">Profil que le pilote applique au jeu ; null = aucun, OptiGame en crée un.</param>
    public static ReversibleChange Change(Guid profileId, string gameName, string exePath, string? driverProfile, uint? current, uint fps)
    {
        var profile = driverProfile is null
            ? $"nouveau profil « OptiGame - {Path.GetFileName(exePath)} » (supprimé si l'on annule)"
            : $"profil « {driverProfile} »";
        return new ReversibleChange
        {
            Id = ChangeId(profileId),
            Title = fps == 0 ? $"{gameName} : aucun plafond de FPS (pilote NVIDIA)" : $"{gameName} : plafond de {fps} FPS (pilote NVIDIA)",
            What = $"Pilote NVIDIA, {profile}, réglage « Frame Rate Limiter » : {Describe(current)} → {Describe(fps)}.",
            Why = "Un plafond juste sous la fréquence de l'écran garde le jeu dans la plage de G-Sync / FreeSync (images régulières, " +
                  "sans la latence de la V-Sync) et évite à la carte graphique de calculer des images inutiles (moins de chaleur et de " +
                  "bruit). Si le jeu propose son propre limiteur ou NVIDIA Reflex, préférez-les : leur latence est plus faible. " +
                  "Pris en compte au prochain lancement du jeu.",
            Writes = [new SettingWrite(KnownSettings.NvidiaFrameRateLimit(exePath), SettingValue.DWord(fps))],
        };
    }
}
