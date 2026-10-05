using OptiGame.Core.State;
using OptiGame.Core.Text;

namespace OptiGame.Core.Playtime;

/// <summary>Une partie. <see cref="EndedAt"/> null = en cours, ou fin inconnue si <see cref="Incomplete"/>.</summary>
public sealed class PlaySession
{
    public required Guid ProfileId { get; set; }

    public required string GameName { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>OptiGame s'est arrêté pendant la partie et le jeu était fermé à son retour : durée inconnue.</summary>
    public bool Incomplete { get; set; }

    public TimeSpan? Duration => EndedAt is { } end && !Incomplete ? end - StartedAt : null;
}

public sealed class PlaytimeDocument
{
    public int Version { get; set; } = 1;

    public List<PlaySession> Sessions { get; set; } = [];
}

public sealed record PlaytimeStats(TimeSpan Total, int SessionCount, DateTimeOffset? LastPlayed)
{
    public static readonly PlaytimeStats None = new(TimeSpan.Zero, 0, null);
}

/// <summary>
/// Temps de jeu, enregistré en « write-ahead » comme le reste : la session est écrite (ouverte) dès le début de la
/// partie, puis fermée à la fin. Aucune durée n'est inventée quand la fin est inconnue.
/// </summary>
public sealed class PlaytimeStore(IStateStore<PlaytimeDocument> store)
{
    private readonly Lock _lock = new();
    private readonly PlaytimeDocument _document = store.Load() ?? new PlaytimeDocument();

    public event EventHandler? Changed;

    public PlaySession? OpenSession
    {
        get { lock (_lock) return _document.Sessions.LastOrDefault(s => s.EndedAt is null && !s.Incomplete); }
    }

    /// <summary>Début de partie. Une session restée ouverte (crash sans reprise) est d'abord marquée incomplète.</summary>
    public void Start(Guid profileId, string gameName, DateTimeOffset at)
    {
        lock (_lock)
        {
            MarkOpenIncomplete();
            _document.Sessions.Add(new PlaySession { ProfileId = profileId, GameName = gameName, StartedAt = at });
            store.Save(_document);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Fin de partie. <paramref name="endKnown"/> = false après un crash : la durée est inconnue.</summary>
    public void End(DateTimeOffset at, bool endKnown = true)
    {
        lock (_lock)
        {
            if (_document.Sessions.LastOrDefault(s => s.EndedAt is null && !s.Incomplete) is not { } open) return;
            if (endKnown) open.EndedAt = at;
            else open.Incomplete = true;
            store.Save(_document);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Au démarrage d'OptiGame, après la récupération des sessions : une session ouverte qui ne correspond pas à la
    /// partie reprise (ou alors qu'aucune partie n'est reprise) n'a pas de fin connue.
    /// </summary>
    public void ReconcileAtStartup(Guid? resumedProfileId)
    {
        lock (_lock)
        {
            if (_document.Sessions.LastOrDefault(s => s.EndedAt is null && !s.Incomplete) is not { } open) return;
            if (resumedProfileId == open.ProfileId) return;
            open.Incomplete = true;
            store.Save(_document);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public PlaytimeStats StatsFor(Guid profileId, DateTimeOffset now)
    {
        lock (_lock)
        {
            var sessions = _document.Sessions.Where(s => s.ProfileId == profileId).ToList();
            if (sessions.Count == 0) return PlaytimeStats.None;

            // Une partie en cours compte déjà jusqu'à maintenant.
            var total = sessions.Sum(s => (s.Duration ?? (s.EndedAt is null && !s.Incomplete ? now - s.StartedAt : TimeSpan.Zero)).Ticks);
            return new PlaytimeStats(TimeSpan.FromTicks(total), sessions.Count, sessions.Max(s => s.StartedAt));
        }
    }

    public IReadOnlyList<PlaySession> RecentSessions(Guid profileId, int count)
    {
        lock (_lock)
        {
            return _document.Sessions.Where(s => s.ProfileId == profileId)
                .OrderByDescending(s => s.StartedAt).Take(count)
                .Select(s => new PlaySession
                {
                    ProfileId = s.ProfileId, GameName = s.GameName, StartedAt = s.StartedAt, EndedAt = s.EndedAt, Incomplete = s.Incomplete,
                })
                .ToList();
        }
    }

    private void MarkOpenIncomplete()
    {
        foreach (var session in _document.Sessions.Where(s => s.EndedAt is null && !s.Incomplete))
        {
            session.Incomplete = true;
        }
    }
}

public static class PlaytimeText
{

    /// <summary>« 12 h 05 », « 42 min », « moins d'1 min ».</summary>
    public static string Duration(TimeSpan duration) => duration.TotalMinutes switch
    {
        < 1 => "moins d'1 min",
        < 60 => $"{(int)duration.TotalMinutes} min",
        _ => $"{(int)duration.TotalHours} h {duration.Minutes:00}",
    };

    /// <summary>« aujourd'hui », « hier », « il y a 3 jours », « le 12 mars 2026 » (FrenchText.Date).</summary>
    public static string LastPlayed(DateTimeOffset when, DateTimeOffset now)
    {
        var days = (now.Date - when.Date).Days;
        return days switch
        {
            <= 0 => "aujourd'hui",
            1 => "hier",
            < 30 => $"il y a {days} jours",
            _ => "le " + FrenchText.Date(when.DateTime),
        };
    }
}
