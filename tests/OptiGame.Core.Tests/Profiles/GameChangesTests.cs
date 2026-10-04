using OptiGame.Core.Gpu;
using OptiGame.Core.Profiles;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Tests.Profiles;

public sealed class GameChangesTests
{
    private static ChangeRecord Change(string id) => new(id, id, "", false, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Only_the_changes_made_for_this_game_are_returned()
    {
        var game = Guid.NewGuid();
        var other = Guid.NewGuid();
        var active = new[]
        {
            Change("fix.game-mode"),
            Change(GameGraphics.ChangeId(game)),
            Change(FrameRateCap.ChangeId(other)),
            Change(FrameRateCap.ChangeId(game)),
        };

        Assert.Equal([GameGraphics.ChangeId(game), FrameRateCap.ChangeId(game)], GameChanges.Of(active, game).Select(c => c.Id));
        Assert.Equal([FrameRateCap.ChangeId(other)], GameChanges.Of(active, other).Select(c => c.Id));
        Assert.Empty(GameChanges.Of(active, Guid.NewGuid()));
    }

    [Fact]
    public void A_diagnostic_fix_is_never_taken_for_a_game_change()
    {
        var game = Guid.NewGuid();

        Assert.Empty(GameChanges.Of([Change($"fix.something.{game:N}")], game));
    }
}
