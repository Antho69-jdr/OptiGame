using OptiGame.Core.Sessions;

namespace OptiGame.Core.Playtime;

/// <summary>
/// Enregistre le temps de jeu à partir des sessions détectées. À brancher AVANT GameSessionManager.Recover, pour
/// entendre la fin d'une session interrompue par un crash.
/// </summary>
public sealed class PlaytimeTracker
{
    private readonly GameSessionManager _sessions;
    private readonly PlaytimeStore _store;
    private readonly TimeProvider _time;

    public PlaytimeTracker(GameSessionManager sessions, PlaytimeStore store, TimeProvider time)
    {
        _sessions = sessions;
        _store = store;
        _time = time;
        sessions.SessionStarted += (_, report) => store.Start(report.Profile.Id, report.Profile.Name, time.GetLocalNow());
        // Session restaurée après un crash : la partie s'est terminée pendant qu'OptiGame était arrêté, fin inconnue.
        sessions.SessionEnded += (_, report) => store.End(time.GetLocalNow(), endKnown: !report.AfterCrash);
    }

    /// <summary>À appeler après GameSessionManager.Recover.</summary>
    public void ReconcileAtStartup() => _store.ReconcileAtStartup(_sessions.Current?.Profile.Id);

    public PlaytimeStats StatsFor(Guid profileId) => _store.StatsFor(profileId, _time.GetLocalNow());
}
