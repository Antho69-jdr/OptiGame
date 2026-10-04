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
        INotificationService notifications, IDialogService dialogs, FileLog log)
    {
        _store = store;
        _settings = settings;
        _notifications = notifications;
        _dialogs = dialogs;
        _log = log;
        watcher.ManifestsChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => _ = CheckAsync());
        watcher.Start();
        _ = CheckAsync();
    }

    public ObservableCollection<NewGameProposalViewModel> Proposals { get; } = [];

    public bool HasProposals => Proposals.Count > 0;

    /// <summary>Un jeu proposé vient d'être ajouté (la bibliothèque recharge ses jaquettes).</summary>
    public event EventHandler<Guid>? GameAdded;

    public async Task CheckAsync()
    {
        var profiles = _store.GetAll();
        var known = _settings.Get().SteamKnownAppIds;
        IReadOnlyList<GameLibraryScanner.SteamApp> apps;
        IReadOnlyList<InstalledGame> games;
        try
        {
            (apps, games) = await Task.Run(() =>
            {
                var apps = GameLibraryScanner.SteamApps();
                if (known is null) return (apps, (IReadOnlyList<InstalledGame>)[]);
                var profileAppIds = profiles.Select(p => GameLibraryScanner.SteamAppIdFor(p, apps)).OfType<string>().ToHashSet();
                var ids = NewSteamGames.ToPropose(apps.Select(a => new SteamInstall(a.AppId, a.StateFlags)), known, profileAppIds);
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
            // Premier passage : les jeux déjà installés sont mémorisés, pas proposés.
            _settings.Update(s => s.SteamKnownAppIds = apps.Select(a => a.AppId).Distinct().ToList());
            _log.Info($"Nouveaux jeux Steam : {apps.Count} jeu(x) déjà installé(s) mémorisé(s), seuls les prochains seront proposés.");
            return;
        }

        // Rien de nouveau : on ne touche pas au bandeau (chaque changement fait relire les bibliothèques à « Mes jeux »).
        if (Proposals.Select(p => p.Game.SteamAppId).SequenceEqual(games.Select(g => g.SteamAppId))) return;

        Proposals.Clear();
        foreach (var game in games)
        {
            Proposals.Add(new NewGameProposalViewModel(game));
            if (game.SteamAppId is { } id && _notified.Add(id))
            {
                _log.Info($"Nouveau jeu Steam installé : {game.Name} ({id}), proposé dans « Mes jeux ».");
                _notifications.Show("Nouveau jeu installé", $"{game.Name} : ajoutez-le à « Mes jeux » depuis OptiGame.");
            }
        }
        OnPropertyChanged(nameof(HasProposals));
    }

    [RelayCommand]
    private void Add(NewGameProposalViewModel? proposal)
    {
        if (proposal?.Game is not { SteamAppId: { } appId } game) return;
        if (game.Candidates.FirstOrDefault() is not { } exe)
        {
            _dialogs.ShowError($"Aucun exécutable trouvé pour {game.Name} dans {game.Folder}. Ajoutez-le avec « Ajouter un jeu ».");
            return;
        }

        var profile = new GameProfile { Name = LibraryViewModel.CleanName(game.Name), ExePath = exe.Path, SteamAppId = appId };
        try
        {
            _store.Save(profile);
        }
        catch (ProfileValidationException ex)
        {
            _dialogs.ShowError($"{game.Name} n'a pas été ajouté :\n\n{ex.Message}");
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
        OnPropertyChanged(nameof(HasProposals));
    }
}

public sealed class NewGameProposalViewModel(InstalledGame game)
{
    public InstalledGame Game { get; } = game;

    public string Name => LibraryViewModel.CleanName(Game.Name);

    public string Detail => Game.Candidates.FirstOrDefault() is { } exe
        ? $"Exécutable : {Path.GetFileName(exe.Path)} (modifiable ensuite dans la page du jeu)"
        : "Aucun exécutable trouvé pour l'instant.";
}
