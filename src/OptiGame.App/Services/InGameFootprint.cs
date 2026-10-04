using System.Windows;
using OptiGame.Core.Logging;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;

namespace OptiGame.App.Services;

/// <summary>
/// Pendant une partie, OptiGame se fait le plus léger possible. Réglage « LightDuringGames » (activé par défaut) : fenêtre
/// principale FERMÉE (et non masquée : tout son contenu est libéré ; elle revient à la fin de la partie si elle était ouverte,
/// même page, même place) et dock fermé (DockController). Toujours : cache d'images vidé et un passage du ramasse-miettes
/// (<see cref="MemoryRelief"/>), travail de fond reporté à la fin de la partie (<see cref="GameTimeGate"/>). La détection du
/// jeu, la restauration des réglages et la mesure automatique ne changent pas.
/// </summary>
public sealed class InGameFootprint(GameSessionManager sessions, AppSettingsStore settings, MemoryRelief relief, FileLog log)
{
    private bool _reopenWindow;

    public void Start(App app)
    {
        sessions.SessionStarted += (_, _) => OnUi(() => EnterGame(app));
        sessions.SessionEnded += (_, _) => OnUi(() => LeaveGame(app));
    }

    /// <summary>Début de partie ; aussi appelé au démarrage d'OptiGame quand une partie interrompue par son plantage a repris.</summary>
    public void EnterGame(App app)
    {
        if (sessions.Current is null) return; // partie déjà finie
        var light = settings.Get().LightDuringGames;
        if (light) _reopenWindow |= app.CloseMainWindowForGame();
        relief.Release(light
            ? "Partie en cours : fenêtre et dock d'OptiGame fermés, jaquettes libérées"
            : "Partie en cours : jaquettes libérées (fenêtre et dock gardés, réglage « Pendant les parties » désactivé)");
    }

    private void LeaveGame(App app)
    {
        if (sessions.Current is not null || !_reopenWindow) return;
        _reopenWindow = false;
        log.Info("Fin de partie : fenêtre d'OptiGame rouverte.");
        app.ReopenMainWindowAfterGame();
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}
