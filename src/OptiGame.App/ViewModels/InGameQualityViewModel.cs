using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.InGame;
using OptiGame.Core.Logging;
using OptiGame.Core.Profiles;
using OptiGame.Core.Rating;

namespace OptiGame.App.ViewModels;

/// <summary>Un niveau de qualité du jeu, avec le nom qu'il lui donne.</summary>
public sealed record QualityLevelOption(int Level, string Label);

/// <summary>
/// « Qualité graphique (fichier du jeu) », onglet Optimisation de la fiche d'un jeu Unreal Engine : tous les groupes de qualité du
/// GameUserSettings.ini mis au niveau choisi. « Appliquer… » passe par la confirmation et le journal des optimisations
/// (« Restaurer l'original… » ici ou depuis le Diagnostic) ; refusé jeu ouvert. L'état est toujours relu dans le fichier.
/// </summary>
public sealed partial class InGameQualityViewModel(GameProfile profile, InGameQualityService service, IDialogService dialogs, FileLog log,
    Action changed) : ObservableObject
{
    private InGameQualitySnapshot? _snapshot;

    /// <summary>Carte affichée seulement pour un jeu Unreal dont les groupes de qualité sont lisibles.</summary>
    [ObservableProperty] private bool _isAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(UndoCommand))]
    private bool _isBusy = true;

    [ObservableProperty] private IReadOnlyList<QualityLevelOption> _levels = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private QualityLevelOption? _selectedLevel;

    [ObservableProperty] private string _currentText = "";
    [ObservableProperty] private string _appliedText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _hasAppliedChange;

    /// <summary>Niveau correspondant au réglage conseillé par la note (null = rien à proposer).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestion), nameof(UseSuggestionLabel))]
    private QualityLevelOption? _suggestedLevel;

    public bool HasSuggestion => SuggestedLevel is not null && SuggestedLevel.Level != _snapshot?.Settings?.QualityLevel;

    public string UseSuggestionLabel => SuggestedLevel is null ? "" : $"Utiliser le réglage conseillé ({SuggestedLevel.Label})";

    private GraphicsPreset? _recommended;

    /// <summary>Réglage conseillé par la note du jeu (appelé à chaque nouvelle note).</summary>
    public void SetRecommended(GraphicsPreset? preset)
    {
        _recommended = preset;
        SuggestedLevel = preset is { } p && _snapshot?.Settings?.Scale is { } scale ? Levels.FirstOrDefault(l => l.Level == UnrealQuality.LevelFor(scale, p)) : null;
    }

    [RelayCommand]
    private void UseSuggestion() => SelectedLevel = SuggestedLevel;

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            _snapshot = await Task.Run(() => service.Read(profile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"Réglages du jeu « {profile.Name} » illisibles", ex);
            IsAvailable = false;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        var settings = _snapshot.Settings;
        IsAvailable = settings is { Scale: not null, QualityKeys.Count: > 0, QualityLevel: not null };
        if (!IsAvailable || settings!.Scale is not { } scale) return;
        Levels = [.. scale.Levels.Select((l, i) => new QualityLevelOption(i, $"{l.Name} (niveau {i})"))];
        SelectedLevel = Levels[settings.QualityLevel!.Value];
        CurrentText = $"Qualité actuelle dans le jeu : {UnrealQuality.LevelLabel(scale, settings.QualityLevel.Value)}, " +
                      $"{settings.QualityKeys!.Count} groupes de qualité (enregistré {Core.Text.FrenchText.When(settings.SavedAt, DateTime.Now)}).";
        HasAppliedChange = _snapshot.AppliedChange is not null;
        AppliedText = _snapshot.AppliedChange is { } applied
            ? $"Réglé par OptiGame {Core.Text.FrenchText.When(applied.AppliedAt.ToLocalTime().DateTime, DateTime.Now)}."
            : "";
        SetRecommended(_recommended);
        OnPropertyChanged(nameof(HasSuggestion));
    }

    private bool CanApply() => !IsBusy && SelectedLevel is not null && SelectedLevel.Level != _snapshot?.Settings?.QualityLevel;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_snapshot?.Settings is not { } settings || SelectedLevel is not { } level) return;
        if (service.IsRunning(profile))
        {
            dialogs.ShowInfo("Jeu ouvert", $"Fermez {profile.Name} d'abord : en quittant, il réécrirait son fichier de réglages et annulerait ce changement.");
            return;
        }
        var change = UnrealQuality.Change(profile.Id, profile.Name, settings, level.Level);
        if (!dialogs.ConfirmChange(change, isAdvanced: false)) return;
        await RunAsync(() => service.Apply(profile, change), $"Qualité graphique de « {profile.Name} » : {level.Label}");
    }

    private bool CanUndo() => !IsBusy && HasAppliedChange;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (_snapshot?.AppliedChange is not { } applied || !dialogs.ConfirmUndo(applied)) return;
        await RunAsync(() =>
        {
            var report = service.Undo(profile);
            if (!report.Success) throw new InvalidOperationException(string.Join("\n", report.Failed.Select(f => $"• {f.Target} : {f.Error}")));
        }, $"Qualité graphique de « {profile.Name} » rétablie");
    }

    private async Task RunAsync(Action action, string done)
    {
        IsBusy = true;
        try
        {
            await Task.Run(action);
            log.Info(done);
        }
        catch (GameRunningException ex)
        {
            dialogs.ShowInfo("Jeu ouvert", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            log.Error($"Qualité graphique de « {profile.Name} » non modifiée", ex);
            dialogs.ShowError("Réglage du jeu non modifié", "Le fichier de réglages du jeu n'a pas pu être modifié.", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
        await LoadAsync();
        changed();
    }
}
