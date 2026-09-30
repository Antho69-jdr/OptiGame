using OptiGame.Core.Playtime;
using OptiGame.Core.State;
using OptiGame.Core.Tests.Fakes;

namespace OptiGame.Core.Tests.Playtime;

public sealed class PlaytimeTests : IDisposable
{
    private static readonly TimeSpan Paris = TimeSpan.FromHours(2);
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 20, 0, 0, Paris);
    private readonly TempDirectory _dir = new();
    private readonly Guid _overwatch = Guid.NewGuid();
    private readonly Guid _pubg = Guid.NewGuid();

    public void Dispose() => _dir.Dispose();

    private PlaytimeStore Open() => new(new JsonStateStore<PlaytimeDocument>(_dir.File("playtime.json")));

    [Fact]
    public void Sessions_add_up_per_game()
    {
        var store = Open();
        store.Start(_overwatch, "Overwatch", T0);
        store.End(T0.AddMinutes(90));
        store.Start(_pubg, "PUBG", T0.AddHours(2));
        store.End(T0.AddHours(2).AddMinutes(30));
        store.Start(_overwatch, "Overwatch", T0.AddDays(1));
        store.End(T0.AddDays(1).AddMinutes(45));

        var stats = Open().StatsFor(_overwatch, T0.AddDays(2));

        Assert.Equal(TimeSpan.FromMinutes(135), stats.Total);
        Assert.Equal(2, stats.SessionCount);
        Assert.Equal(T0.AddDays(1), stats.LastPlayed);
        Assert.Equal(PlaytimeStats.None, Open().StatsFor(Guid.NewGuid(), T0));
    }

    [Fact]
    public void Open_session_is_persisted_immediately_and_counts_until_now()
    {
        Open().Start(_overwatch, "Overwatch", T0);

        var reopened = Open(); // redémarrage d'OptiGame pendant la partie
        Assert.Equal(_overwatch, reopened.OpenSession?.ProfileId);
        Assert.Equal(TimeSpan.FromMinutes(20), reopened.StatsFor(_overwatch, T0.AddMinutes(20)).Total);
    }

    [Fact]
    public void Crash_with_game_closed_marks_session_incomplete_without_inventing_a_duration()
    {
        Open().Start(_overwatch, "Overwatch", T0);

        var afterCrash = Open();
        afterCrash.End(T0.AddHours(5), endKnown: false);

        var stats = afterCrash.StatsFor(_overwatch, T0.AddHours(6));
        Assert.Equal(TimeSpan.Zero, stats.Total);
        Assert.Equal(1, stats.SessionCount);
        Assert.Equal(T0, stats.LastPlayed);
        Assert.True(Assert.Single(afterCrash.RecentSessions(_overwatch, 5)).Incomplete);
    }

    [Fact]
    public void Reconcile_keeps_the_resumed_game_and_closes_anything_else()
    {
        Open().Start(_overwatch, "Overwatch", T0);

        var resumed = Open();
        resumed.ReconcileAtStartup(_overwatch);
        Assert.NotNull(resumed.OpenSession);

        resumed.ReconcileAtStartup(null);
        Assert.Null(resumed.OpenSession);
        Assert.True(resumed.RecentSessions(_overwatch, 1)[0].Incomplete);
    }

    [Fact]
    public void Dangling_open_session_is_closed_as_incomplete_when_a_new_game_starts()
    {
        var store = Open();
        store.Start(_overwatch, "Overwatch", T0);
        store.Start(_pubg, "PUBG", T0.AddHours(1));
        store.End(T0.AddHours(2));

        Assert.True(store.RecentSessions(_overwatch, 1)[0].Incomplete);
        Assert.Equal(TimeSpan.FromHours(1), store.StatsFor(_pubg, T0.AddHours(3)).Total);
    }

    [Fact]
    public void End_without_open_session_does_nothing()
    {
        var store = Open();
        store.End(T0);
        Assert.Null(store.OpenSession);
    }

    [Theory]
    [InlineData(0.5, "moins d'1 min")]
    [InlineData(42, "42 min")]
    [InlineData(725, "12 h 05")]
    public void Durations_are_short_and_readable(double minutes, string expected)
    {
        Assert.Equal(expected, PlaytimeText.Duration(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void Last_played_is_relative_then_absolute()
    {
        Assert.Equal("aujourd'hui", PlaytimeText.LastPlayed(T0, T0.AddHours(2)));
        Assert.Equal("hier", PlaytimeText.LastPlayed(T0, T0.AddDays(1)));
        Assert.Equal("il y a 5 jours", PlaytimeText.LastPlayed(T0, T0.AddDays(5)));
        Assert.Equal("le 30 septembre 2026", PlaytimeText.LastPlayed(T0, T0.AddDays(60)));
    }
}
