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
    public void Installed_games_are_proposed_while_my_games_is_empty()
    {
        // Premier passage avec des jeux : l'existant est mémorisé, rien n'est proposé.
        Assert.Null(NewSteamGames.KnownToSkip(known: null, profileCount: 3));
        Assert.Equal(["3832010"], NewSteamGames.KnownToSkip(known: ["3832010"], profileCount: 3));

        // Mes jeux vide (nouvel utilisateur, ou jeux mémorisés en silence par une version précédente) : tout est proposé.
        Assert.Empty(NewSteamGames.KnownToSkip(known: null, profileCount: 0)!);
        Assert.Empty(NewSteamGames.KnownToSkip(known: ["3832010"], profileCount: 0)!);
        var proposed = NewSteamGames.ToPropose([new SteamInstall("3832010", 4)], NewSteamGames.KnownToSkip(["3832010"], 0)!, profileAppIds: []);
        Assert.Equal(["3832010"], proposed);
    }
}
