using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(InstallGpuCommand), nameof(InstallUpdatesCommand))]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(InstallGpuCommand), nameof(InstallUpdatesCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private string _gpuStatus = "";

    [ObservableProperty]
    private string _updatesStatus = "";

    [ObservableProperty]
    private string _hiddenUpdatesNote = "";

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
        IsSearching = true;
        Gpus.Clear();
        foreach (var update in Updates) update.PropertyChanged -= OnUpdateSelectionChanged;
        Updates.Clear();
        HiddenUpdatesNote = "";
        GpuStatus = "Recherche du dernier pilote auprès de NVIDIA…";
        UpdatesStatus = "Recherche dans Windows Update (environ 30 secondes)…";
        try
        {
            // Les deux recherches en parallèle : Windows Update est long, NVIDIA répond en une seconde.
            var updatesTask = Task.Run(windowsUpdate.Search);
            var adapters = await Task.Run(() => gpus.GetAdapters().Where(g => g.IsPhysical).ToList());
            string? nvidiaLatest = null;
            foreach (var gpu in adapters)
            {
                var status = await nvidia.CheckAsync(gpu);
                nvidiaLatest ??= status.Latest?.Version;
                Gpus.Add(new GpuDriverItemViewModel(status, gpu.DriverDate));
            }
            GpuStatus = adapters.Count == 0 ? "Aucune carte graphique physique détectée." : $"Vérifié à {time.GetLocalNow():HH:mm}.";

            try
            {
                var all = await updatesTask;
                var hidden = all.Where(u => DriverRules.IsSupersededByNvidia(u, nvidiaLatest)).ToList();
                foreach (var update in all.Except(hidden))
                {
                    var item = new WindowsUpdateDriverItemViewModel(update);
                    item.PropertyChanged += OnUpdateSelectionChanged;
                    Updates.Add(item);
                }
                UpdatesStatus = Updates.Count == 0
                    ? "Aucun pilote proposé par Windows Update."
                    : $"{Updates.Count} pilote(s) proposé(s) par Windows Update. Cochez ceux à installer.";
                HiddenUpdatesNote = hidden.Count == 0 ? "" : string.Join("\n", hidden.Select(u =>
                    $"Masqué : « {u.Title} » — NVIDIA publie un pilote plus récent ({nvidiaLatest}), inutile d'installer une version plus ancienne."));
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
            {
                log.Error("Recherche des pilotes Windows Update impossible", ex);
                UpdatesStatus = $"Windows Update ne répond pas : {ex.Message}";
            }
        }
        finally
        {
            IsSearching = false;
            InstallUpdatesCommand.NotifyCanExecuteChanged();
        }
    }

    // ---- Installation du pilote NVIDIA ----

    private bool CanInstallGpu(GpuDriverItemViewModel? item) => item is { CanInstall: true } && !IsSearching && !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstallGpu))]
    private async Task InstallGpuAsync(GpuDriverItemViewModel? item)
    {
        if (item?.Status.Latest is not { } latest) return;
        var choice = dialogs.ConfirmDriverInstall(DriverInstallPlans.ForNvidia(item.Status, latest, RestoreAvailability()));
        if (choice is null) return;

        IsInstalling = true;
        var installerRan = false;
        _download = new CancellationTokenSource();
        try
        {
            if (choice.CreateRestorePoint && !await CreateRestorePointAsync($"OptiGame : pilote NVIDIA {latest.Version}", text => item.ProgressText = text))
            {
                return;
            }

            item.IsDownloading = true;
            item.ProgressText = "Téléchargement depuis NVIDIA…";
            var path = await downloader.DownloadNvidiaInstallerAsync(latest.DownloadUrl, new Progress<DownloadProgress>(item.Report), _download.Token);
            item.IsDownloading = false;

            using var installer = await Task.Run(() => privileged.StartVerifiedInstaller(path));
            installerRan = true;
            log.Info($"Installeur NVIDIA {latest.Version} ouvert : {path}");
            item.ProgressText = "Signature vérifiée (NVIDIA Corporation). L'installeur de NVIDIA est ouvert : suivez ses étapes. " +
                                "OptiGame revérifiera le pilote quand il sera fermé.";
            await installer.WaitForExitAsync();
            log.Info($"Installeur NVIDIA fermé (code {installer.ExitCode}).");
        }
        catch (OperationCanceledException)
        {
            item.ProgressText = "Téléchargement annulé.";
        }
        catch (InstallerRejectedException ex)
        {
            item.ProgressText = "Installeur refusé.";
            dialogs.ShowError($"L'installeur a été refusé : il n'a pas été ouvert.\n\n{ex.Message}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            log.Error($"Installation du pilote NVIDIA {latest.Version} impossible", ex);
            item.ProgressText = "Installation interrompue.";
            dialogs.ShowError($"L'installation du pilote NVIDIA n'a pas abouti ; rien n'a été installé par OptiGame.\n\n{ex.Message}");
        }
        finally
        {
            item.IsDownloading = false;
            _download?.Dispose();
            _download = null;
            IsInstalling = false;
        }

        if (installerRan) await SearchAsync(); // version installée à jour
    }

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
            dialogs.ShowInfo((lines.Count == 0 ? "Aucun des pilotes choisis n'est encore proposé par Windows Update." : string.Join("\n", lines)) +
                             (report.RebootRequired ? "\n\nRedémarrez Windows pour terminer l'installation." : ""));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException
                                       or ArgumentException)
        {
            log.Error("Installation de pilotes Windows Update impossible", ex);
            dialogs.ShowError($"Windows Update n'a pas pu installer les pilotes choisis.\n\n{ex.Message}");
        }
        finally
        {
            IsInstalling = false;
        }

        if (ran) await SearchAsync();
    }

    private void OnUpdateSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowsUpdateDriverItemViewModel.IsSelected)) InstallUpdatesCommand.NotifyCanExecuteChanged();
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
        return report.Created || dialogs.Confirm($"{report.Message}\n\nContinuer l'installation sans nouveau point de restauration ?");
    }

    [RelayCommand]
    private static void OpenLink(Uri? url)
    {
        // Pages officielles uniquement (notes de version NVIDIA) ; explorer.exe les ouvre sans droits administrateur.
        if (url is null || url.Scheme != Uri.UriSchemeHttps || !url.Host.EndsWith("nvidia.com", StringComparison.OrdinalIgnoreCase)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", url.ToString()) { UseShellExecute = true });
    }
}

public sealed partial class GpuDriverItemViewModel(GpuDriverStatus status, DateTime? installedDate) : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public GpuDriverStatus Status { get; } = status;

    public string Name => Status.GpuName;

    public string StateLabel => Status.State switch
    {
        DriverState.UpdateAvailable => "Mise à jour disponible",
        DriverState.UpToDate => "À jour",
        _ => "Non vérifié",
    };

    public bool IsUpdateAvailable => Status.State == DriverState.UpdateAvailable;

    public bool IsUpToDate => Status.State == DriverState.UpToDate;

    /// <summary>Installation proposée : nouveau pilote NVIDIA, téléchargeable depuis les serveurs officiels.</summary>
    public bool CanInstall => IsUpdateAvailable && Status.Vendor == GpuVendor.Nvidia && Status.Latest is { } l && InstallerFiles.FileNameFor(l.DownloadUrl) is not null;

    public string InstallLabel => $"Télécharger et installer le {Status.Latest?.Version}";

    public string Message => Status.Message;

    public string Installed => $"Installé : {Status.InstalledVersion ?? "?"}" +
                               (installedDate is { } d ? $" du {d.ToString("d MMMM yyyy", Fr)}" : "");

    public bool HasLatest => Status.Latest is not null;

    public string Latest => Status.Latest is { } l
        ? $"Dernier publié par NVIDIA : {l.Version}" + (l.ReleaseDate is { } r ? $" du {r.ToString("d MMMM yyyy", Fr)}" : "") +
          (l.SizeText is { } size ? $" ({DriverInstallPlans.FrenchSize(size)})" : "")
        : "";

    public Uri? DetailsUrl => Status.Latest?.DetailsUrl;

    public bool HasDetails => DetailsUrl is not null;

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
        Update.DriverDate is { } d ? $"du {d:dd/MM/yyyy}" : null,
        Update.SizeBytes is > 0 and var size ? $"{size / 1048576.0:N1} Mo" : null,
        Update.MayRequireReboot ? "redémarrage possible" : null,
    }.OfType<string>());
}
