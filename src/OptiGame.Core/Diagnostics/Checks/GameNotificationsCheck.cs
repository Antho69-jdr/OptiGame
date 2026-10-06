namespace OptiGame.Core.Diagnostics.Checks;

/// <summary>
/// « Ne pas déranger » automatique pendant les jeux (Windows 11 : Paramètres › Système › Notifications). Toujours « À savoir » :
/// Windows range ce réglage dans son CloudStore (HKCU\…\CloudStore\…\windows.data.donotdisturb.quietmoment$quietmomentgame),
/// dans un format binaire NON documenté — relevé le 2026-10-06 : les 7 règles automatiques y ont le même contenu de 13 octets,
/// leur état n'y est donc pas lisible de façon fiable. OptiGame ne le lit ni ne l'écrit : il explique et ouvre le bon écran.
/// </summary>
public sealed class GameNotificationsCheck : IDiagnosticCheck
{
    public string Id => "windows.game-notifications";

    public string Title => "Notifications pendant les jeux";

    public DiagnosticResult Run() => new()
    {
        CheckId = Id,
        Title = Title,
        Status = DiagnosticStatus.Info,
        Summary = "Windows peut couper les notifications pendant vos parties : vérifiez que c'est activé.",
        Explanation =
            "Une notification qui s'affiche en pleine partie peut faire sortir du jeu ou provoquer une saccade. Windows 11 active " +
            "« Ne pas déranger » tout seul quand un jeu tourne, si la case « Lors de l'utilisation d'un jeu » est cochée dans " +
            "Paramètres › Système › Notifications › Activer automatiquement Ne pas déranger (Windows 10 : Assistant de concentration " +
            "› « Quand je joue »). C'est le réglage par défaut, mais certains outils d'optimisation le décochent.",
        Details = ["Windows garde ce réglage dans un format non documenté : OptiGame ne le lit ni ne le modifie."],
        Link = new DiagnosticLink("Ouvrir les paramètres des notifications", DiagnosticLinkTarget.WindowsNotificationSettings),
    };
}
