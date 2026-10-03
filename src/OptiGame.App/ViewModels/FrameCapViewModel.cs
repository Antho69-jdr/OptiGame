using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Gpu;
using OptiGame.Core.Logging;
using OptiGame.Core.Profiles;

namespace OptiGame.App.ViewModels;

/// <summary>
/// Carte « Pilote NVIDIA » de la page du jeu : plafond de FPS dans le profil du pilote. « Appliquer » passe par la confirmation
/// habituelle et le journal des corrections (annulable ici ou depuis le Diagnostic). L'état affiché est toujours relu dans le pilote.
/// </summary>
public sealed partial class FrameCapViewModel(GameProfile profile, FrameCapService service, IDialogService dialogs, FileLog log) : ObservableObject
{
    private FrameCapSnapshot? _snapshot;

    /// <summary>Carte affichée seulement si le pilote NVIDIA est installé.</summary>
    public bool IsAvailable { get; } = FrameCapService.IsAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(UndoCommand))]
    private bool _isBusy = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(InputError))]
    private string _fpsText = "";

    [ObservableProperty] private string _profileText = "Lecture du pilote NVIDIA…";
    [ObservableProperty] private string _currentText = "";
    [ObservableProperty] private string _suggestionText = "";
    [ObservableProperty] private string _appliedText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _hasAppliedChange;

    private int? Fps => int.TryParse(FpsText.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var fps) ? fps : null;

    public string InputError => FpsText.Length == 0 ? "" : FrameRateCap.Validate(Fps) ?? "";

    public async Task LoadAsync()
    {
        if (!IsAvailable) return;
        IsBusy = true;
        try
        {
            _snapshot = await Task.Run(() => service.Read(profile));
        }
        catch (Exception ex) when (ex is Platform.Gpu.NvidiaApiException or System.Management.ManagementException or InvalidOperationException)
        {
            log.Error($"Profil NVIDIA de « {profile.Name} » illisible", ex);
            ProfileText = $"Profil du pilote NVIDIA illisible : {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        var setting = _snapshot.Setting;
        ProfileText = setting.ProfileName is { } name
            ? $"Profil du pilote pour ce jeu : « {name} »{(setting.ProfileIsPredefined ? " (créé par NVIDIA)" : "")}."
            : $"Aucun profil du pilote pour ce jeu (le profil global s'applique) : OptiGame créera « OptiGame - {Path.GetFileName(profile.ExePath)} ».";
        CurrentText = setting.Value is { } own
            ? $"Plafond actuel : {FrameRateCap.Describe(own)} (défini dans ce profil)."
            : setting.EffectiveValue is { } inherited and > 0
                ? $"Plafond actuel : {FrameRateCap.Describe(inherited)} (hérité du profil global)."
                : "Plafond actuel : aucun.";
        var suggested = FrameRateCap.Suggested(_snapshot.RefreshHz);
        SuggestionText = $"Conseillé : {suggested} FPS (écran à {_snapshot.RefreshHz} Hz − 3), surtout avec G-Sync / FreeSync. " +
                         "Si le jeu a son propre limiteur ou NVIDIA Reflex, préférez-les. 0 = aucun plafond.";
        FpsText = (setting.Value is { } v ? (int)v : suggested).ToString(CultureInfo.CurrentCulture);
        HasAppliedChange = _snapshot.AppliedChange is not null;
        AppliedText = _snapshot.AppliedChange is { } applied
            ? $"Réglé par OptiGame le {applied.AppliedAt.ToLocalTime():dd/MM/yyyy à HH:mm}. « Annuler » rétablit la valeur d'origine."
            : "";
    }

    private bool CanApply() => !IsBusy && _snapshot is not null && FrameRateCap.Validate(Fps) is null && (uint)Fps!.Value != (_snapshot.Setting.Value ?? 0);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_snapshot is not { } snapshot || Fps is not { } fps) return;
        var change = FrameRateCap.Change(profile.Id, profile.Name, profile.ExePath, snapshot.Setting.ProfileName, snapshot.Setting.Value, (uint)fps);
        if (!dialogs.ConfirmChange(change, isAdvanced: false)) return;
        await RunAsync(() => service.Apply(change), $"Plafond de FPS de « {profile.Name} » : {FrameRateCap.Describe((uint)fps)}");
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
        }, $"Plafond de FPS de « {profile.Name} » rétabli");
    }

    private async Task RunAsync(Action action, string done)
    {
        IsBusy = true;
        try
        {
            await Task.Run(action);
            log.Info(done);
        }
        catch (Exception ex) when (ex is Platform.Gpu.NvidiaApiException or InvalidOperationException or ArgumentException or IOException)
        {
            log.Error($"Plafond de FPS de « {profile.Name} » non modifié", ex);
            dialogs.ShowError($"Le pilote NVIDIA n'a pas accepté la modification.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
        await LoadAsync();
    }
}
