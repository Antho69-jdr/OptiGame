using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Gpu;
using OptiGame.Core.Logging;
using OptiGame.Core.Profiles;

namespace OptiGame.App.ViewModels;

/// <summary>
/// « DLSS (pilote NVIDIA) », onglet Optimisation de la fiche : version de DLSS livrée avec le jeu, et « Imposer le modèle le plus
/// récent… » par le profil du pilote (confirmé, journal des optimisations, « Restaurer l'original… »). Carte affichée seulement si
/// le jeu contient DLSS ou si un remplacement est déjà défini. État toujours relu.
/// </summary>
public sealed partial class DlssOverrideViewModel(GameProfile profile, DlssOverrideService service, IDialogService dialogs, FileLog log) : ObservableObject
{
    private DlssSnapshot? _snapshot;

    [ObservableProperty] private bool _isAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(UndoCommand))]
    private bool _isBusy = true;

    [ObservableProperty] private string _libraryText = "";
    [ObservableProperty] private string _currentText = "";
    [ObservableProperty] private string _noteText = "";
    [ObservableProperty] private string _appliedText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _hasAppliedChange;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isLatest;

    public async Task LoadAsync()
    {
        if (!DlssOverrideService.IsAvailable) return;
        IsBusy = true;
        try
        {
            _snapshot = await Task.Run(() => service.Read(profile));
        }
        catch (Exception ex) when (ex is Platform.Gpu.NvidiaApiException or IOException or UnauthorizedAccessException)
        {
            log.Error($"DLSS de « {profile.Name} » illisible", ex);
            IsAvailable = false;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        var snapshot = _snapshot;
        var overrideValue = snapshot.Override.EffectiveValue;
        var preset = snapshot.Preset.EffectiveValue;
        IsAvailable = snapshot.Library is not null || overrideValue == DlssOverride.On;
        if (!IsAvailable) return;

        LibraryText = snapshot.Library is { } library
            ? $"DLSS livré avec le jeu : version {library.Version} ({Path.GetFileName(Path.GetDirectoryName(library.Path))}\\{Platform.Gpu.DlssLibrary.FileName})."
            : "Bibliothèque DLSS du jeu introuvable dans son dossier.";
        CurrentText = $"Modèle DLSS : {DlssOverride.Describe(overrideValue, preset)}" +
                      (snapshot.Override.ProfileName is { } name ? $" — profil NVIDIA « {name} »." : ".");
        IsLatest = overrideValue == DlssOverride.On && preset == DlssOverride.LatestPreset;
        NoteText = snapshot.DriverSupports
            ? "N'agit que si le DLSS est activé dans les options du jeu. Réglé dans le pilote : les fichiers du jeu ne sont pas " +
              "modifiés, et une mise à jour du jeu ne l'annule pas."
            : "Ce pilote NVIDIA ne propose pas le remplacement du DLSS : mettez-le à jour (page Pilotes).";
        HasAppliedChange = snapshot.AppliedChange is not null;
        AppliedText = snapshot.AppliedChange is { } applied
            ? $"Réglé par OptiGame {Core.Text.FrenchText.When(applied.AppliedAt.ToLocalTime().DateTime, DateTime.Now)}."
            : "";
    }

    private bool CanApply() => !IsBusy && _snapshot is { DriverSupports: true } && !IsLatest;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_snapshot is not { } snapshot) return;
        var change = DlssOverride.Change(profile.Id, profile.Name, profile.ExePath, snapshot.Override.ProfileName,
            DlssOverride.Describe(snapshot.Override.EffectiveValue, snapshot.Preset.EffectiveValue));
        if (!dialogs.ConfirmChange(change, isAdvanced: false)) return;
        await RunAsync(() => service.Apply(change), $"DLSS de « {profile.Name} » : modèle le plus récent imposé");
    }

    private bool CanUndo() => !IsBusy && HasAppliedChange;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (_snapshot?.AppliedChange is not { } applied || !dialogs.ConfirmUndo(applied)) return;
        await RunAsync(() =>
        {
            var report = service.Undo(profile.Id);
            if (!report.Success) throw new InvalidOperationException(string.Join("\n", report.Failed.Select(f => $"• {f.Target} : {f.Error}")));
        }, $"DLSS de « {profile.Name} » rétabli");
    }

    private async Task RunAsync(Action action, string done)
    {
        IsBusy = true;
        try
        {
            await Task.Run(action);
            log.Info(done);
        }
        catch (Exception ex) when (ex is Platform.Gpu.NvidiaApiException or InvalidOperationException or ArgumentException)
        {
            log.Error($"DLSS de « {profile.Name} » non modifié", ex);
            dialogs.ShowError("DLSS non modifié", "Le pilote NVIDIA n'a pas accepté la modification : le réglage du pilote est inchangé.", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
        await LoadAsync();
    }
}
