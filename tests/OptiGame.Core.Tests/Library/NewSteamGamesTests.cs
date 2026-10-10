using OptiGame.Core.Library;

namespace OptiGame.Core.Tests.Library;

public sealed class NewSteamGamesTests
{
    [Theory]
    [InlineData(4, true)]      // installé (tous les jeux de la machine de dev)
    [InlineData(6, true)]      // installé, mise à jour à faire
    [InlineData(1026, false)]  // téléchargement en cours
    [InlineData(2, false)]
    [InlineData(null, false)]
    public void Only_fully_installed_games_count(int? flags, bool installed)
    {
        Assert.Equal(installed, NewSteamGames.IsFullyInstalled(flags));
    }

    [Fact]
    public void Proposes_only_new_installed_games_without_profile()
    {
        var installed = new[]
        {
            new SteamInstall("578080", 4),   // PUBG : déjà un profil
            new SteamInstall("3832010", 4),  // SaveSync : déjà vu (ignoré ou mémorisé au premier passage)
            new SteamInstall("1145350", 4),  // nouveau, installé
            new SteamInstall("2694490", 1026), // nouveau, encore en téléchargement
        };

        var proposed = NewSteamGames.ToPropose(installed, known: ["3832010"], profileAppIds: ["578080"]);

        Assert.Equal(["1145350"], proposed);
    }

    [Fact]
    public void Games_already_seen_are_not_proposed_again()
    {
        // Les jeux déjà installés sont montrés par la section « Installés, pas encore dans Mes jeux », plus par le bandeau
        // (au premier lancement, il les proposait en double, outils compris : audit du 2026-10-10).
        var proposed = NewSteamGames.ToPropose([new SteamInstall("3832010", 4), new SteamInstall("1145350", 4)], known: ["3832010"], profileAppIds: []);
        Assert.Equal(["1145350"], proposed);
    }
}
