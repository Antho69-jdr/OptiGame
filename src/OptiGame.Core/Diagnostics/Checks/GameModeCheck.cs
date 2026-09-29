using OptiGame.Core.Changes;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Diagnostics.Checks;

public sealed class GameModeCheck(SettingAccessors settings) : IDiagnosticCheck
{
    public string Id => "windows.game-mode";

    public string Title => "Mode Jeu de Windows";

    public DiagnosticResult Run()
    {
        var value = settings.Read(KnownSettings.GameMode);
        var detail = $"AutoGameModeEnabled = {value}";

        if (value.AsDWord() == 0)
        {
            return new DiagnosticResult
            {
                CheckId = Id,
                Title = Title,
                Status = DiagnosticStatus.NeedsAttention,
                Summary = "Le Mode Jeu est désactivé.",
                Explanation = Explanation,
                Details = [detail],
                Fixes =
                [
                    new DiagnosticFix(new ReversibleChange
                    {
                        Id = "fix.windows.game-mode",
                        Title = "Activer le Mode Jeu",
                        What = @"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled : 0 → 1.",
                        Why = "Le Mode Jeu donne la priorité au jeu et évite les installations de mises à jour pendant la partie.",
                        Writes = [new SettingWrite(KnownSettings.GameMode, SettingValue.DWord(1))],
                    }),
                ],
            };
        }

        return new DiagnosticResult
        {
            CheckId = Id,
            Title = Title,
            Status = DiagnosticStatus.Ok,
            Summary = value.IsAbsent ? "Le Mode Jeu est activé (réglage par défaut de Windows)." : "Le Mode Jeu est activé.",
            Explanation = Explanation,
            Details = [detail],
        };
    }

    private const string Explanation =
        "Le Mode Jeu donne la priorité au jeu en cours (ressources CPU/GPU), empêche Windows Update d'installer des " +
        "pilotes ou de redémarrer, et limite les notifications pendant la partie. Il est activé par défaut ; sur un " +
        "Windows récent, il n'y a pas de raison de le désactiver.";
}
