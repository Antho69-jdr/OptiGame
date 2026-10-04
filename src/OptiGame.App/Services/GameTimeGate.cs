using System.Windows;
using OptiGame.Core.Sessions;

namespace OptiGame.App.Services;

/// <summary>
/// Pendant une partie, le travail de fond qui peut attendre (relecture des bibliothèques, jaquettes à télécharger, notes des
/// jeux, nouveaux jeux Steam, temps de jeu Steam) n'est pas fait : il est noté, une fois par sorte de travail, et fait à la fin
/// de la partie. Thread UI uniquement.
/// </summary>
public sealed class GameTimeGate
{
    private readonly GameSessionManager _sessions;
    private readonly Dictionary<string, Action> _pending = [];

    public GameTimeGate(GameSessionManager sessions)
    {
        _sessions = sessions;
        sessions.SessionEnded += (_, _) => Application.Current?.Dispatcher.BeginInvoke(RunPending);
    }

    public bool InGame => _sessions.Current is not null;

    /// <summary>Fait le travail tout de suite, ou à la fin de la partie en cours (le dernier demandé pour chaque clé).</summary>
    public void RunOrDefer(string key, Action work)
    {
        if (InGame) _pending[key] = work;
        else work();
    }

    private void RunPending()
    {
        if (InGame || _pending.Count == 0) return; // une autre partie a déjà commencé : ce sera à sa fin
        var pending = _pending.Values.ToList();
        _pending.Clear();
        foreach (var work in pending) work();
    }
}
