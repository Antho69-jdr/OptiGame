using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Controls;
using OptiGame.Core.Text;
using OptiGame.App.Services;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Drivers;
using OptiGame.Core.Logging;
using OptiGame.Platform.Drivers;
using OptiGame.Platform.Privileged;

namespace OptiGame.App.ViewModels;

/// <summary>
/// Page « Pilotes » : pilote graphique comparé au dernier publié par NVIDIA, et pilotes proposés par Windows Update.
/// La recherche est en lecture seule (lancée à la première ouverture, puis à la demande). L'installation ne se fait que
/// sur action explicite, après une confirmation qui dit qu'OptiGame ne pourra pas l'annuler.
/// </summary>
public sealed partial class DriversViewModel(
    IGpuInfoProvider gpus,
    NvidiaDriverClient nvidia,
    AmdChipsetClient chipsetClient,
    WindowsUpdateDriverSearch windowsUpdate,
    DriverDownloader downloader,
    IPrivilegedOperations privileged,
    IDialogService dialogs,
    FileLog log,
    TimeProvider time) : ObservableObject
{
    private bool _searchedOnce;
    private CancellationTokenSource? _download;

    public ObservableCollection<GpuDriverItemViewModel> Gpus { get; } = [];

    public ObservableCollection<WindowsUpdateDriverItemViewModel> Updates { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(InstallGpuCommand), nameof(InstallChipsetCommand), nameof(InstallUpdatesCommand), nameof(InstallItemCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditUpdates))]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(InstallGpuCommand), nameof(InstallChipsetCommand), nameof(InstallUpdatesCommand), nameof(InstallItemCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditUpdates))]
    private bool _isInstalling;

    /// <summary>Cases de Windows Update modifiables (pas pendant une recherche ni une installation).</summary>
    public bool CanEditUpdates => !IsSearching && !IsInstalling;

    /// <summary>Logiciel de chipset AMD ; null sur un PC sans processeur AMD (la carte est alors masquée).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChipset))]
    [NotifyCanExecuteChangedFor(nameof(InstallChipsetCommand))]
    private ChipsetDriverItemViewModel? _chipset;

    public bool HasChipset => Chipset is not null || ChipsetStatus.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChipset))]
    private string _chipsetStatus = "";

    [ObservableProperty]
    private string _gpuStatus = "";

    [ObservableProperty]
    private string _updatesStatus = "";

    [ObservableProperty]
    private string _hiddenUpdatesNote = "";

    /// <summary>Pilotes plus récents disponibles (carte graphique, chipset) après la dernière recherche : badge de la navigation.</summary>
    [ObservableProperty]
    private int _availableCount;

    /// <summary>Première ouverture de la page : la recherche démarre toute seule.</summary>
    public void EnsureSearched()
    {
        if (_searchedOnce) return;
        _searchedOnce = true;
        _ = SearchAsync();
    }

    private bool CanSearch() => !IsSearching && !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        // Les résultats précédents restent affichés (atténués) jusqu'aux nouveaux : la page ne saute pas.
        IsSearching = true;
        GpuStatus = "Recherche des derniers pilotes…";
        UpdatesStatus = "Recherche dans Windows Update (environ 30 secondes)…";
        try
        {
            // Recherches en parallèle : Windows Update est long, NVIDIA et AMD répondent en quelques secondes.
            var updatesTask = Task.Run(windowsUpdate.Search);
            var chipsetTask = Task.Run(() => chipsetClient.CheckAsync());
            var adapters = await Task.Run(() => gpus.GetAdapters().Where(g => g.IsPhysical).ToList());
            string? nvidiaLatest = null;
            var found = new List<GpuDriverItemViewModel>();
            foreach (var gpu in adapters)
            {
                var status = await nvidia.CheckAsync(gpu);
                nvidiaLatest ??= status.Latest?.Version;
                found.Add(new GpuDriverItemViewModel(status, gpu.DriverDate));
            }
            Gpus.Clear();
            foreach (var item in found) Gpus.Add(item);
            GpuStatus = adapters.Count == 0 ? "Aucune carte graphique physique détectée." : $"Vérifié à {time.GetLocalNow():HH:mm}.";

            try
            {
                if (await chipsetTask is { } chipset)
                {
                    Chipset = new ChipsetDriverItemViewModel(chipset);
                    ChipsetStatus = $"Vérifié à {time.GetLocalNow():HH:mm}.";
                }
                else
                {
                    Chipset = null;
                    ChipsetStatus = "";
                }
            }
            catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                log.Error("Vérification du chipset AMD impossible", ex);
                ChipsetStatus = "Vérification du chipset impossible pour l'instant : le détail est dans le journal.";
            }

            try
            {
                var all = await updatesTask;
                var hidden = all.Where(u => DriverRules.IsSupersededByNvidia(u, nvidiaLatest)).ToList();
                foreach (var update in Updates) update.PropertyChanged -= OnUpdateSelectionChanged;
                Updates.Clear();
                foreach (var update in all.Except(hidden))
                {
                    var item = new WindowsUpdateDriverItemViewModel(update);
                    item.PropertyChanged += OnUpdateSelectionChanged;
                    Updates.Add(item);
                }
                UpdatesStatus = Updates.Count == 0
                    ? "Aucun pilote proposé par Windows Update."
                    : $"{FrenchText.Count(Updates.Count, "pilote proposé", "pilotes proposés")} par Windows Update. Cochez ceux à installer.";
                HiddenUpdatesNote = hidden.Count == 0 ? "" : string.Join("\n", hidden.Select(u =>
                    $"Masqué : « {u.Title} » — NVIDIA publie un pilote plus récent ({nvidiaLatest}), inutile d'installer une version plus ancienne."));
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
            {
                log.Error("Recherche des pilotes Windows Update impossible", ex);
                UpdatesStatus = "Windows Update ne répond pas pour l'instant : réessayez plus tard (Actualiser). Le détail est dans le journal.";
            }
        }
        finally
        {
            IsSearching = false;
            AvailableCount = Gpus.Count(g => g.IsUpdateAvailable) + (Chipset is { IsUpdateAvailable: true } ? 1 : 0);
            HasUpdates = Updates.Count > 0;
            UpdateInstallLabel();
            InstallUpdatesCommand.NotifyCanExecuteChanged();
        }
    }

    // ---- Installation du pilote NVIDIA ----

    private bool CanInstallGpu(GpuDriverItemViewModel? item) => item is { CanInstall: true } && !IsSearching && !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstallGpu))]
    private async Task InstallGpuAsync(GpuDriverItemViewModel? item)
    {
        if (item?.Status.Latest is not { } latest) return;
        await InstallOfficialAsync(item, InstallerVendor.Nvidia, latest.DownloadUrl, $"pilote NVIDIA {latest.Version}",
            DriverInstallPlans.ForNvidia(item.Status, latest, RestoreAvailability()));
    }

    // ---- Installation du logiciel de chipset AMD ----

    private bool CanInstallChipset() => Chipset is { CanInstall: true } && !IsSearching && !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstallChipset))]
    private async Task InstallChipsetAsync()
    {
        if (Chipset?.Status.Latest is not { } latest) return;
        await InstallOfficialAsync(Chipset, InstallerVendor.Amd, latest.DownloadUrl, $"logiciel de chipset AMD {latest.Version}",
            DriverInstallPlans.ForAmdChipset(Chipset.Status, latest, RestoreAvailability()));
    }

    /// <summary>
    /// Installeur officiel d'un fabricant : confirmation (non annulable par OptiGame), point de restauration éventuel,
    /// téléchargement depuis son serveur, signature vérifiée, ouverture de son installeur, puis nouvelle vérification.
    /// </summary>
    private async Task InstallOfficialAsync(InstallableDriverViewModel item, InstallerVendor vendor, Uri url, string label, DriverInstallPlan plan)
    {
        var choice = dialogs.ConfirmDriverInstall(plan);
        if (choice is null) return;

        var vendorName = OfficialInstallers.SignerName(vendor);
        IsInstalling = true;
        var installerRan = false;
        _download = new CancellationTokenSource();
        try
        {
            if (choice.CreateRestorePoint && !await CreateRestorePointAsync($"OptiGame : {label}", text => item.ProgressText = text))
            {
                return;
            }

            item.IsDownloading = true;
            item.ProgressText = $"Téléchargement depuis {url.Host}…";
            var path = await downloader.DownloadInstallerAsync(vendor, url, new Progress<DownloadProgress>(item.Report), _download.Token);
            item.IsDownloading = false;

            using var installer = await Task.Run(() => privileged.StartVerifiedInstaller(path, vendor));
            installerRan = true;
            log.Info($"Installeur ouvert ({label}) : {path}");
            item.ProgressText = $"Signature vérifiée ({vendorName}). L'installeur officiel est ouvert : suivez ses étapes. " +
                                "OptiGame revérifiera quand il sera fermé.";
            await installer.WaitForExitAsync();
            log.Info($"Installeur fermé ({label}, code {installer.ExitCode}).");
        }
        catch (OperationCanceledException)
        {
            item.ProgressText = "Téléchargement annulé.";
        }
        catch (InstallerRejectedException ex)
        {
            item.ProgressText = "Installeur refusé.";
            dialogs.ShowError("Installeur refusé",
                "Le fichier téléchargé n'a pas passé la vérification de signature : il n'a pas été ouvert et rien n'a été installé.", ex.Message);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            log.Error($"Installation impossible ({label})", ex);
            item.ProgressText = "Installation interrompue.";
            dialogs.ShowError("Installation interrompue",
                $"L'installation du {label} n'a pas abouti : rien n'a été installé par OptiGame. Vérifiez la connexion à Internet, puis réessayez.",
                ex.Message);
        }
        finally
        {
            item.IsDownloading = false;
            _download?.Dispose();
            _download = null;
            IsInstalling = false;
        }

        if (!installerRan) return;
        await SearchAsync(); // versions installées à jour
        // Bilan : l'installeur a pu être annulé ou échouer sans rien dire.
        var after = item is ChipsetDriverItemViewModel ? Chipset : (InstallableDriverViewModel?)Gpus.FirstOrDefault(g => g.Name == item.Name);
        if (after is { IsUpToDate: true })
        {
            ShowOutcome(Severity.Success, $"{after.Name} : pilote à jour", "Un redémarrage de Windows peut être demandé par l'installeur.");
        }
        else
        {
            ShowOutcome(Severity.Warning, "Installation non constatée",
                $"La version installée n'a pas changé ({after?.InstalledVersionText ?? "inconnue"}). L'installation a peut-être été annulée ; vous pouvez la relancer.");
        }
    }

    /// <summary>Bilan de la dernière installation (InfoBar fermable).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutcome))]
    private string _outcomeTitle = "";

    [ObservableProperty] private string _outcomeMessage = "";
    [ObservableProperty] private Severity _outcomeSeverity = Severity.Success;

    public bool HasOutcome => OutcomeTitle.Length > 0;

    [RelayCommand]
    private void DismissOutcome() => OutcomeTitle = "";

    private void ShowOutcome(Severity severity, string title, string message)
    {
        OutcomeSeverity = severity;
        OutcomeMessage = message;
        OutcomeTitle = title;
    }

    /// <summary>Bouton d'installation commun aux cartes (carte graphique NVIDIA, chipset AMD).</summary>
    [RelayCommand(CanExecute = nameof(CanInstallItem))]
    private Task InstallItemAsync(InstallableDriverViewModel? item) => item switch
    {
        GpuDriverItemViewModel gpu => InstallGpuAsync(gpu),
        ChipsetDriverItemViewModel => InstallChipsetAsync(),
        _ => Task.CompletedTask,
    };

    private bool CanInstallItem(InstallableDriverViewModel? item) => item is { CanInstall: true } && !IsSearching && !IsInstalling;

    [RelayCommand]
    private void CancelDownload() => _download?.Cancel();

    // ---- Installation par Windows Update ----

    private bool CanInstallUpdates() => Updates.Any(u => u.IsSelected) && !IsSearching && !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstallUpdates))]
    private async Task InstallUpdatesAsync()
    {
        var selected = Updates.Where(u => u.IsSelected).Select(u => u.Update).ToList();
        var choice = dialogs.ConfirmDriverInstall(DriverInstallPlans.ForWindowsUpdate(selected, RestoreAvailability()));
        if (choice is null) return;

        IsInstalling = true;
        var ran = false;
        try
        {
            if (choice.CreateRestorePoint && !await CreateRestorePointAsync("OptiGame : pilotes Windows Update", text => UpdatesStatus = text))
            {
                return;
            }

            var report = await Task.Run(() => privileged.InstallWindowsUpdateDrivers(
                selected.Select(s => s.UpdateId).ToList(),
                text => Application.Current?.Dispatcher.BeginInvoke(() => UpdatesStatus = text)));
            ran = true;

            var lines = report.Results.Select(r => $"• {r.Title} : {r.Describe()}")
                .Concat(report.Skipped.Select(s => $"• {s}"))
                .ToList();
            log.Info("Installation Windows Update : " + string.Join(" ; ", lines) + (report.RebootRequired ? " (redémarrage requis)" : ""));
            ShowOutcome(report.RebootRequired ? Severity.Warning : Severity.Success,
                report.RebootRequired ? "Installation terminée : redémarrage requis" : "Installation terminée",
                (lines.Count == 0 ? "Aucun des pilotes choisis n'est encore proposé par Windows Update." : string.Join("\n", lines)) +
                (report.RebootRequired ? "\n\nRedémarrez Windows pour terminer l'installation." : ""));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException
                                       or ArgumentException)
        {
            log.Error("Installation de pilotes Windows Update impossible", ex);
            dialogs.ShowError("Installation impossible", "Windows Update n'a pas pu installer les pilotes choisis : rien n'a été installé.", ex.Message);
        }
        finally
        {
            IsInstalling = false;
        }

        if (ran) await SearchAsync();
    }

    private void OnUpdateSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WindowsUpdateDriverItemViewModel.IsSelected)) return;
        UpdateInstallLabel();
        InstallUpdatesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Liste Windows Update non vide : la consigne et le bouton d'installation s'affichent.</summary>
    [ObservableProperty]
    private bool _hasUpdates;

    /// <summary>« Installer les 2 pilotes… » (le nombre cochés) ; « Installer… » sans sélection (bouton inactif).</summary>
    [ObservableProperty]
    private string _installUpdatesLabel = "Installer…";

    private void UpdateInstallLabel()
    {
        var count = Updates.Count(u => u.IsSelected);
        InstallUpdatesLabel = count switch
        {
            0 => "Installer…",
            1 => "Installer le pilote coché…",
            _ => $"Installer les {count} pilotes cochés…",
        };
    }

    /// <summary>Protection du système : état réel écrit dans le journal (il n'est lisible qu'avec les droits administrateur).</summary>
    private RestorePointAvailability RestoreAvailability()
    {
        var availability = SystemRestore.Availability();
        log.Info($"Protection du système (points de restauration) : {availability}.");
        return availability;
    }

    /// <summary>Point de restauration avant l'installation ; si Windows ne le crée pas, l'utilisateur décide de continuer ou non.</summary>
    private async Task<bool> CreateRestorePointAsync(string description, Action<string> status)
    {
        status("Création d'un point de restauration du système…");
        RestorePointReport report;
        try
        {
            report = await Task.Run(() => privileged.CreateRestorePoint(description));
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            report = new RestorePointReport(false, $"Point de restauration impossible : {ex.Message}");
        }
        log.Info($"Point de restauration « {description} » : {report.Message}");
        status(report.Message);
        return report.Created || dialogs.Confirm("Point de restauration non créé",
            $"{report.Message}\n\nVous pouvez installer quand même : le retour au pilote précédent restera possible depuis le " +
            "Gestionnaire de périphériques (« Restaurer le pilote »).",
            "Installer sans point de restauration", isDestructive: true);
    }

    [RelayCommand]
    private static void OpenLink(Uri? url)
    {
        // Pages officielles uniquement (notes de version NVIDIA et AMD) ; explorer.exe les ouvre sans droits administrateur.
        if (url is null || url.Scheme != Uri.UriSchemeHttps) return;
        if (!url.Host.EndsWith("nvidia.com", StringComparison.OrdinalIgnoreCase) && !url.Host.EndsWith("amd.com", StringComparison.OrdinalIgnoreCase)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", url.ToString()) { UseShellExecute = true });
    }
}

/// <summary>Élément dont l'installeur officiel peut être téléchargé : progression et message affichés sous l'élément.</summary>
public abstract partial class InstallableDriverViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>Avancement du téléchargement, de 0 à 100.</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgressText))]
    private string _progressText = "";

    public bool HasProgressText => ProgressText.Length > 0;

    public void Report(DownloadProgress progress)
    {
        const double MiB = 1024 * 1024;
        if (progress.Total is > 0 and var total)
        {
            Progress = progress.Received * 100.0 / total;
            ProgressText = $"Téléchargement : {Progress:0} % ({progress.Received / MiB:N0} / {total / MiB:N0} Mo)";
        }
        else
        {
            ProgressText = $"Téléchargement : {progress.Received / MiB:N0} Mo";
        }
    }

    protected static string StateText(DriverState state) => state switch
    {
        DriverState.UpdateAvailable => "Mise à jour disponible",
        DriverState.UpToDate => "À jour",
        _ => "Non vérifié",
    };

    // Carte commune (carte graphique, chipset) : un seul modèle d'affichage dans DriversView.
    public abstract string Name { get; }

    public abstract string StateLabel { get; }

    public abstract bool IsUpdateAvailable { get; }

    public abstract bool IsUpToDate { get; }

    public bool IsUnknown => !IsUpdateAvailable && !IsUpToDate;

    public abstract bool CanInstall { get; }

    public abstract string InstallLabel { get; }

    public abstract string Message { get; }

    /// <summary>« Installé : 591.86 du 22 septembre 2026 ».</summary>
    public abstract string Installed { get; }

    /// <summary>Version installée seule (bilan après installation).</summary>
    public abstract string InstalledVersionText { get; }

    public abstract bool HasLatest { get; }

    /// <summary>« Disponible : 617.14 du 22 septembre 2026 (750 Mo) ».</summary>
    public abstract string Latest { get; }

    public abstract Uri? DetailsUrl { get; }

    public abstract string DetailsLabel { get; }

    public bool HasDetails => DetailsUrl is not null;

    /// <summary>« Carte graphique, Mise à jour disponible » (lecteurs d'écran).</summary>
    public string AccessibleName => $"{Name}, {StateLabel}";
}

public sealed partial class GpuDriverItemViewModel(GpuDriverStatus status, DateTime? installedDate) : InstallableDriverViewModel
{

    public GpuDriverStatus Status { get; } = status;

    public override string Name => Status.GpuName;

    public override string StateLabel => StateText(Status.State);

    public override bool IsUpdateAvailable => Status.State == DriverState.UpdateAvailable;

    public override bool IsUpToDate => Status.State == DriverState.UpToDate;

    /// <summary>Installation proposée : nouveau pilote NVIDIA, téléchargeable depuis les serveurs officiels.</summary>
    public override bool CanInstall => IsUpdateAvailable && Status.Vendor == GpuVendor.Nvidia && Status.Latest is { } l && InstallerFiles.FileNameFor(l.DownloadUrl) is not null;

    public override string InstallLabel => $"Installer le pilote {Status.Latest?.Version}…";

    public override string Message => Status.Message;

    public override string Installed => $"Installé : {InstalledVersionText}" +
                                        (installedDate is { } d ? $" du {FrenchText.Date(d)}" : "");

    public override string InstalledVersionText => Status.InstalledVersion ?? "version inconnue";

    public override bool HasLatest => Status.Latest is not null;

    public override string Latest => Status.Latest is { } l
        ? $"Disponible chez NVIDIA : {l.Version}" + (l.ReleaseDate is { } r ? $" du {FrenchText.Date(r)}" : "") +
          (l.SizeText is { } size ? $" ({DriverInstallPlans.FrenchSize(size)})" : "")
        : "";

    public override Uri? DetailsUrl => Status.Latest?.DetailsUrl;

    public override string DetailsLabel => "Notes de version (nvidia.com)";
}

/// <summary>Logiciel de chipset AMD (carte mère).</summary>
public sealed class ChipsetDriverItemViewModel(ChipsetDriverStatus status) : InstallableDriverViewModel
{

    public ChipsetDriverStatus Status { get; } = status;

    public override string Name => Status.Description;

    public override string StateLabel => StateText(Status.State);

    public override bool IsUpdateAvailable => Status.State == DriverState.UpdateAvailable;

    public override bool IsUpToDate => Status.State == DriverState.UpToDate;

    /// <summary>Installation proposée : version plus récente, téléchargeable depuis drivers.amd.com.</summary>
    public override bool CanInstall => IsUpdateAvailable && Status.Latest is { } l && OfficialInstallers.FileNameFor(InstallerVendor.Amd, l.DownloadUrl) is not null;

    public override string InstallLabel => $"Installer le logiciel de chipset {Status.Latest?.Version}…";

    public override string Message => Status.Message;

    public override string Installed => Status.InstalledText;

    public override string InstalledVersionText => Status.InstalledText;

    public override bool HasLatest => Status.Latest is not null;

    public override string Latest => Status.Latest is { } l
        ? $"Disponible chez AMD : AMD Chipset Software {l.Version}" + (l.ReleaseDate is { } r ? $" du {FrenchText.Date(r)}" : "") +
          (l.SizeText is { } size ? $" ({DriverInstallPlans.FrenchSize(size)})" : "")
        : "";

    /// <summary>Notes de version si AMD les publie, sinon la page de téléchargement du chipset.</summary>
    public override Uri? DetailsUrl => Status.Latest?.ReleaseNotes ?? Status.SupportPage;

    public override string DetailsLabel => Status.Latest?.ReleaseNotes is not null ? "Notes de version (amd.com)" : "Page des pilotes (amd.com)";
}

public sealed partial class WindowsUpdateDriverItemViewModel(WindowsUpdateDriver update) : ObservableObject
{
    public WindowsUpdateDriver Update { get; } = update;

    public string Title => Update.Title;

    /// <summary>Aucun pilote n'est coché d'office : l'utilisateur choisit.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public string Details => string.Join(" · ", new[]
    {
        Update.DriverClass is { } c ? $"Catégorie : {c}" : null,
        Update.DriverDate is { } d ? $"du {FrenchText.Date(d)}" : null,
        Update.SizeBytes is > 0 and var size ? $"{size / 1048576.0:N1} Mo" : null,
        Update.MayRequireReboot ? "redémarrage possible" : null,
    }.OfType<string>());
}
