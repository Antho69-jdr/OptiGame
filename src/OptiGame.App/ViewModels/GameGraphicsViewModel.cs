using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Logging;
using OptiGame.Core.Profiles;
using OptiGame.Core.Settings;

namespace OptiGame.App.ViewModels;

public sealed record AutoHdrOption(AutoHdrChoice Value, string Label);

public sealed record GpuOption(GpuChoice Value, string Label);

/// <summary>
/// Carte « Graphismes (Windows) » de la page du jeu : Auto HDR et carte graphique pour ce jeu. L'état affiché est relu
/// dans Windows ; « Appliquer » passe par la confirmation habituelle et le journal des corrections (annulable).
/// </summary>
public sealed partial class GameGraphicsViewModel(GameProfile profile, GameGraphicsService service, IDialogService dialogs, FileLog log) : ObservableObject
{
    private GameGraphicsSnapshot? _snapshot;

    public IReadOnlyList<AutoHdrOption> AutoHdrOptions { get; } =
    [
        new(AutoHdrChoice.Windows, "Réglage de Windows"),
        new(AutoHdrChoice.On, "Activé"),
        new(AutoHdrChoice.Off, "Désactivé"),
    ];

    public IReadOnlyList<GpuOption> GpuOptions { get; } =
    [
        new(GpuChoice.Windows, "Laisser Windows décider"),
        new(GpuChoice.HighPerformance, "Carte la plus puissante"),
        new(GpuChoice.PowerSaving, "Carte économe en énergie"),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private AutoHdrOption? _selectedAutoHdr;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private GpuOption? _selectedGpu;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy = true;

    [ObservableProperty] private string _currentText = "Lecture des réglages de Windows…";
    [ObservableProperty] private string _hdrText = "";
    [ObservableProperty] private bool _showGpu;
    [ObservableProperty] private string _gpuNote = "";
    [ObservableProperty] private string _appliedText = "";

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            _snapshot = await Task.Run(() => service.Read(profile));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or System.Management.ManagementException)
        {
            log.Error($"Réglages graphiques de « {profile.Name} » illisibles", ex);
            CurrentText = $"Réglages de Windows illisibles : {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        var state = _snapshot.State;
        SelectedAutoHdr = AutoHdrOptions.First(o => o.Value == state.AutoHdr);
        SelectedGpu = GpuOptions.First(o => o.Value == state.Gpu);
        CurrentText = "Actuellement : Auto HDR " + state.AutoHdr switch
        {
            AutoHdrChoice.On => "activé",
            AutoHdrChoice.Off => "désactivé",
            _ when state.AutoHdrRaw is { } raw => $"réglé par Windows (valeur {raw}, non documentée par Microsoft)",
            _ => "réglé par Windows (réglage global)",
        } + (_snapshot.PhysicalGpus.Count > 1 ? $" ; carte graphique : {SelectedGpu.Label.ToLowerInvariant()}." : ".");

        var hdr = _snapshot.Displays.Where(d => d.HdrEnabled).Select(d => d.Name).ToList();
        HdrText = hdr.Count > 0
            ? $"HDR activé sur : {string.Join(", ", hdr)}. L'Auto HDR agit sur ces écrans."
            : _snapshot.Displays.Any(d => d.HdrSupported)
                ? "Aucun écran n'a le HDR activé : l'Auto HDR n'aura d'effet qu'après l'avoir activé (Paramètres > Affichage > HDR)."
                : "Aucun écran HDR détecté : l'Auto HDR n'a pas d'effet sur ce PC.";

        ShowGpu = _snapshot.PhysicalGpus.Count > 1;
        GpuNote = _snapshot.PhysicalGpus.Count == 1
            ? $"Une seule carte graphique ({_snapshot.PhysicalGpus[0]}) : le choix de la carte est sans objet sur ce PC."
            : "";
        AppliedText = _snapshot.AppliedChange is { } applied
            ? $"Réglé par OptiGame le {applied.AppliedAt.ToLocalTime():dd/MM/yyyy à HH:mm}. « Réglage de Windows » + Appliquer (ou le Diagnostic) rétablit la valeur d'origine."
            : "";
    }

    private bool CanApply() =>
        !IsBusy && _snapshot is { } snapshot && SelectedAutoHdr is { } hdr && SelectedGpu is { } gpu &&
        (hdr.Value != snapshot.State.AutoHdr || gpu.Value != snapshot.State.Gpu ||
         // « Réglage de Windows » alors qu'OptiGame a modifié la valeur : l'annulation reste possible.
         (snapshot.AppliedChange is not null && hdr.Value == AutoHdrChoice.Windows && gpu.Value == GpuChoice.Windows));

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_snapshot is not { } snapshot || SelectedAutoHdr is not { } hdr || SelectedGpu is not { } gpu) return;

        var change = GameGraphics.Change(profile.Id, profile.Name, snapshot.Targets, hdr.Value, ShowGpu ? gpu.Value : GpuChoice.Windows);
        IsBusy = true;
        try
        {
            if (change is null)
            {
                if (snapshot.AppliedChange is not { } applied || !dialogs.ConfirmUndo(applied)) return;
                var report = await Task.Run(() => service.Undo(profile.Id));
                if (!report.Success)
                {
                    dialogs.ShowError("La valeur d'origine n'a pas pu être rétablie :\n" + string.Join("\n", report.Failed.Select(f => $"• {f.Target} : {f.Error}")));
                }
            }
            else if (dialogs.ConfirmChange(change, isAdvanced: false))
            {
                await Task.Run(() => service.Apply(change));
                log.Info($"{change.Title} ({string.Join(" ; ", change.Writes.Select(w => $"{w.Target.Name} = {w.NewValue.Text}"))})");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            log.Error($"Réglages graphiques de « {profile.Name} » non appliqués", ex);
            dialogs.ShowError($"Le réglage n'a pas pu être appliqué ; rien n'a été modifié.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
        await LoadAsync();
    }
}
