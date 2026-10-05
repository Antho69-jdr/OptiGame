using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Logging;
using OptiGame.Core.Profiles;
using OptiGame.Core.Settings;

namespace OptiGame.App.ViewModels;

public sealed record GpuOption(GpuChoice Value, string Label);

/// <summary>
/// Carte « Graphismes (Windows) » de la page du jeu. Auto HDR : affiché seulement (encodage de Windows non documenté),
/// réglé dans les paramètres de Windows. Carte graphique (PC à plusieurs cartes) : « Appliquer » passe par la
/// confirmation habituelle et le journal des corrections (annulable). L'état affiché est toujours relu dans Windows.
/// </summary>
public sealed partial class GameGraphicsViewModel(GameProfile profile, GameGraphicsService service, IDialogService dialogs, FileLog log) : ObservableObject
{
    private GameGraphicsSnapshot? _snapshot;

    public IReadOnlyList<GpuOption> GpuOptions { get; } =
    [
        new(GpuChoice.Windows, "Laisser Windows décider"),
        new(GpuChoice.HighPerformance, "Carte la plus puissante"),
        new(GpuChoice.PowerSaving, "Carte économe en énergie"),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private GpuOption? _selectedGpu;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy = true;

    [ObservableProperty] private string _autoHdrText = "Lecture des réglages de Windows…";
    [ObservableProperty] private string _hdrText = "";
    [ObservableProperty] private bool _showGpu;
    [ObservableProperty] private string _gpuNote = "";
    [ObservableProperty] private string _appliedText = "";

    /// <summary>Résumé d'une ligne pour la liste de la fiche du jeu.</summary>
    [ObservableProperty] private string _summary = "Lecture des réglages de Windows…";

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
            AutoHdrText = $"Réglages de Windows illisibles : {ex.Message}";
            Summary = "Réglages de Windows illisibles";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        AutoHdrText = _snapshot.State.AutoHdrRaw is { } raw
            ? $"Auto HDR de ce jeu : géré par Windows (valeur {raw}, encodage non documenté par Microsoft). Pour le changer : bouton ci-dessous, puis ce jeu dans la liste."
            : "Auto HDR de ce jeu : réglage global de Windows. Pour le régler jeu par jeu : bouton ci-dessous.";

        var hdr = _snapshot.Displays.Where(d => d.HdrEnabled).Select(d => d.Name).ToList();
        HdrText = hdr.Count > 0
            ? $"HDR activé sur : {string.Join(", ", hdr)}. L'Auto HDR agit sur ces écrans."
            : _snapshot.Displays.Any(d => d.HdrSupported)
                ? "Aucun écran n'a le HDR activé : l'Auto HDR n'aura d'effet qu'après l'avoir activé (Paramètres > Affichage > HDR)."
                : "Aucun écran HDR détecté : l'Auto HDR n'a pas d'effet sur ce PC.";

        ShowGpu = _snapshot.PhysicalGpus.Count > 1;
        Summary = (_snapshot.State.AutoHdrRaw is not null ? "Auto HDR réglé pour ce jeu dans Windows" : "Auto HDR : réglage global de Windows") +
                  (ShowGpu ? $" · {GpuOptions.First(o => o.Value == _snapshot.State.Gpu).Label.ToLowerInvariant()}" : "");
        SelectedGpu = GpuOptions.First(o => o.Value == _snapshot.State.Gpu);
        GpuNote = _snapshot.PhysicalGpus.Count == 1
            ? $"Une seule carte graphique ({_snapshot.PhysicalGpus[0]}) : le choix de la carte est sans objet sur ce PC."
            : "";
        AppliedText = _snapshot.AppliedChange is { } applied
            ? $"Réglé par OptiGame le {applied.AppliedAt.ToLocalTime():dd/MM/yyyy à HH:mm}. « Laisser Windows décider » + Appliquer (ou le Diagnostic) rétablit la valeur d'origine."
            : "";
    }

    [RelayCommand]
    private static void OpenWindowsGraphicsSettings() =>
        // Page « Graphiques » des Paramètres (adresse ms-settings documentée) ; explorer.exe l'ouvre sans droits administrateur.
        Process.Start(new ProcessStartInfo("explorer.exe", "ms-settings:display-advancedgraphics") { UseShellExecute = true });

    private bool CanApply() =>
        !IsBusy && ShowGpu && _snapshot is { } snapshot && SelectedGpu is { } gpu &&
        (gpu.Value != snapshot.State.Gpu || (snapshot.AppliedChange is not null && gpu.Value == GpuChoice.Windows));

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_snapshot is not { } snapshot || SelectedGpu is not { } gpu) return;

        var change = GameGraphics.Change(profile.Id, profile.Name, snapshot.Targets, gpu.Value);
        IsBusy = true;
        try
        {
            if (change is null)
            {
                if (snapshot.AppliedChange is not { } applied || !dialogs.ConfirmUndo(applied)) return;
                var report = await Task.Run(() => service.Undo(profile.Id));
                if (!report.Success)
                {
                    dialogs.ShowError("Restauration impossible", "Le réglage d'origine de la carte graphique n'a pas pu être rétabli : vous pourrez réessayer.",
                        string.Join("\n", report.Failed.Select(f => $"{f.Target} : {f.Error}")));
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
            dialogs.ShowError("Réglage non appliqué", "La carte graphique de ce jeu n'a pas pu être réglée : rien n'a été modifié.", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
        await LoadAsync();
    }
}
