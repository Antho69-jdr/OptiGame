using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;
using OptiGame.Core.Profiles;
using OptiGame.Core.Settings;
using OptiGame.Platform.Library;

namespace OptiGame.App.ViewModels;

/// <summary>
/// Nouveaux jeux Steam proposés dans « Mes jeux » : vérification au démarrage, puis à chaque écriture d'un manifeste
/// Steam (fin d'installation). Rien n'est ajouté sans clic ; un jeu ajouté ou ignoré n'est plus proposé.
/// </summary>
public sealed partial class NewSteamGamesViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly AppSettingsStore _settings;
    private readonly INotificationService _notifications;
    private readonly IDialogService _dialogs;
    private readonly FileLog _log;
    private readonly HashSet<string> _notified = [];

    public NewSteamGamesViewModel(ProfileStore store, AppSettingsStore settings, SteamLibraryWatcher watcher,
        INotificationService notifications, IDialogService dialogs, FileLog log, GameTimeGate gate)
    {
        _store = store;
        _settings = settings;
        _notifications = notifications;
        _dialogs = dialogs;
        _log = log;
        // Steam écrit ses manifestes pendant ses téléchargements, parfois en pleine partie : vérifié à la fin de la partie.
        watcher.ManifestsChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => gate.RunOrDefer("steam-new-games", () => _ = CheckAsync()));
        watcher.Start();
        _ = CheckAsync();
    }

    public ObservableCollection<NewGameProposalViewModel> Proposals { get; } = [];

    public bool HasProposals => Proposals.Count > 0;

    /// <summary>Bandeau groupé de « Mes jeux » : un seul, quel que soit le nombre de jeux proposés.</summary>
    /// <summary>Mes jeux vide : ce sont les jeux Steam déjà installés qui sont proposés, pas seulement les nouveaux.</summary>
    private bool _offersInstalledGames;

    public string BannerTitle => (Proposals.Count, _offersInstalledGames) switch
    {
        (1, false) => $"Nouveau jeu Steam installé : {Proposals[0].Name}",
        (1, true) => $"Jeu Steam installé, pas encore dans Mes jeux : {Proposals[0].Name}",
        (var n, false) => $"{n} nouveaux jeux Steam installés",
        (var n, true) => $"{n} jeux Steam installés, pas encore dans Mes jeux",
    };

    public string BannerMessage => Proposals.Count == 1
        ? Proposals[0].Detail
        : string.Join(", ", Proposals.Select(p => p.Name)) + ". Leur fichier .exe se modifie ensuite dans leur fiche.";

    public string AddAllLabel => Proposals.Count == 1 ? "Ajouter à Mes jeux" : $"Ajouter les {Proposals.Count} jeux";

    /// <summary>Ajoute tous les jeux proposés (rien n'est ajouté sans ce clic).</summary>
    [RelayCommand]
    private void AddAll()
    {
        foreach (var proposal in Proposals.ToList()) Add(proposal);
    }

    /// <summary>« Ne plus proposer » : ces jeux restent ajoutables par « Détecter les jeux installés… ».</summary>
    [RelayCommand]
    private void IgnoreAll()
    {
        foreach (var proposal in Proposals.ToList()) Ignore(proposal);
    }

    private void NotifyProposals()
    {
        OnPropertyChanged(nameof(HasProposals));
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(BannerMessage));
        OnPropertyChanged(nameof(AddAllLabel));
    }

    /// <summary>Un jeu proposé vient d'être ajouté (la bibliothèque recharge ses jaquettes).</summary>
    public event EventHandler<Guid>? GameAdded;

    public async Task CheckAsync()
    {
        var profiles = _store.GetAll();
        var known = _settings.Get().SteamKnownAppIds;
        var skip = NewSteamGames.KnownToSkip(known, profiles.Count);
        IReadOnlyList<GameLibraryScanner.SteamApp> apps;
        IReadOnlyList<InstalledGame> games;
        try
        {
            (apps, games) = await Task.Run(() =>
            {
                var apps = GameLibraryScanner.SteamApps();
                if (skip is null) return (apps, (IReadOnlyList<InstalledGame>)[]);
                var profileAppIds = profiles.Select(p => GameLibraryScanner.SteamAppIdFor(p, apps)).OfType<string>().ToHashSet();
                var ids = NewSteamGames.ToPropose(apps.Select(a => new SteamInstall(a.AppId, a.StateFlags)), skip, profileAppIds);
                return (apps, (IReadOnlyList<InstalledGame>)ids.Select(id => GameLibraryScanner.ToInstalledGame(apps.First(a => a.AppId == id))).ToList());
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            _log.Error("Recherche des nouveaux jeux Steam impossible", ex);
            return;
        }

        if (known is null)
        {
            // Premier passage : les jeux déjà installés sont mémorisés ; proposés quand même si Mes jeux est vide.
            _settings.Update(s => s.SteamKnownAppIds = apps.Select(a => a.AppId).Distinct().ToList());
            _log.Info($"Nouveaux jeux Steam : {apps.Count} jeu(x) déjà installé(s) mémorisé(s), seuls les prochains seront proposés.");
            if (profiles.Count > 0) return;
        }

        var offersInstalledGames = profiles.Count == 0;
        // Rien de nouveau : on ne touche pas au bandeau (chaque changement fait relire les bibliothèques à « Mes jeux »).
        if (offersInstalledGames == _offersInstalledGames && Proposals.Select(p => p.Game.SteamAppId).SequenceEqual(games.Select(g => g.SteamAppId))) return;
        _offersInstalledGames = offersInstalledGames;

        Proposals.Clear();
        foreach (var game in games)
        {
            Proposals.Add(new NewGameProposalViewModel(game));
            // Jeux déjà installés proposés à un Mes jeux vide : le bandeau suffit (pas une notification par jeu).
            if (!_offersInstalledGames && game.SteamAppId is { } id && _notified.Add(id))
            {
                _log.Info($"Nouveau jeu Steam installé : {game.Name} ({id}), proposé dans « Mes jeux ».");
                _notifications.Show("Nouveau jeu installé", $"{game.Name} : ajoutez-le à « Mes jeux » depuis OptiGame.");
            }
        }
        NotifyProposals();
    }

    [RelayCommand]
    private void Add(NewGameProposalViewModel? proposal)
    {
        if (proposal?.Game is not { SteamAppId: { } appId } game) return;
        if (game.Candidates.FirstOrDefault() is not { } exe)
        {
            _dialogs.ShowError($"{game.Name} n'a pas été ajouté", $"Aucun fichier .exe n'a été trouvé dans {game.Folder}. Ajoutez-le avec « Ajouter des jeux » puis « Choisir un fichier .exe… ».");
            return;
        }

        var profile = new GameProfile { Name = LibraryViewModel.CleanName(game.Name), ExePath = exe.Path, SteamAppId = appId };
        try
        {
            _store.Save(profile);
        }
        catch (ProfileValidationException ex)
        {
            _dialogs.ShowError($"{game.Name} n'a pas été ajouté", ex.Message);
            return;
        }
        _log.Info($"Nouveau jeu Steam ajouté : {profile.Name} ({exe.Path}).");
        Forget(proposal, appId);
        GameAdded?.Invoke(this, profile.Id);
    }

    [RelayCommand]
    private void Ignore(NewGameProposalViewModel? proposal)
    {
        if (proposal?.Game.SteamAppId is not { } appId) return;
        _log.Info($"Nouveau jeu Steam ignoré : {proposal.Game.Name} ({appId}).");
        Forget(proposal, appId);
    }

    /// <summary>Jamais reproposé.</summary>
    private void Forget(NewGameProposalViewModel proposal, string appId)
    {
        _settings.Update(s =>
        {
            s.SteamKnownAppIds ??= [];
            if (!s.SteamKnownAppIds.Contains(appId)) s.SteamKnownAppIds.Add(appId);
        });
        Proposals.Remove(proposal);
        NotifyProposals();
    }
}

public sealed class NewGameProposalViewModel(InstalledGame game)
{
    public InstalledGame Game { get; } = game;

    public string Name => LibraryViewModel.CleanName(Game.Name);

    public string Detail => Game.Candidates.FirstOrDefault() is { } exe
        ? $"Fichier .exe : {Path.GetFileName(exe.Path)} (modifiable ensuite dans sa fiche)"
        : "Aucun exécutable trouvé pour l'instant.";
}
