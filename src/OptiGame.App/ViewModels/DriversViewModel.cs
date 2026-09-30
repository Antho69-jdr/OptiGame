using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Drivers;
using OptiGame.Core.Logging;
using OptiGame.Platform.Drivers;

namespace OptiGame.App.ViewModels;

/// <summary>
/// Page « Pilotes » : pilote graphique comparé au dernier publié par NVIDIA, et pilotes proposés par Windows Update.
/// Lecture seule : rien n'est téléchargé ni installé. Recherche lancée à la première ouverture de la page, puis à la demande.
/// </summary>
public sealed partial class DriversViewModel(
    IGpuInfoProvider gpus,
    NvidiaDriverClient nvidia,
    WindowsUpdateDriverSearch windowsUpdate,
    FileLog log,
    TimeProvider time) : ObservableObject
{
    private bool _searchedOnce;

    public ObservableCollection<GpuDriverItemViewModel> Gpus { get; } = [];

    public ObservableCollection<WindowsUpdateDriverItemViewModel> Updates { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private bool _isSearching;

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

    private bool CanSearch() => !IsSearching;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsSearching = true;
        Gpus.Clear();
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
                foreach (var update in all.Except(hidden)) Updates.Add(new WindowsUpdateDriverItemViewModel(update));
                UpdatesStatus = Updates.Count == 0 ? "Aucun pilote proposé par Windows Update." : $"{Updates.Count} pilote(s) proposé(s) par Windows Update.";
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
        }
    }

    [RelayCommand]
    private static void OpenLink(Uri? url)
    {
        // Pages officielles uniquement (notes de version NVIDIA) ; explorer.exe les ouvre sans droits administrateur.
        if (url is null || url.Scheme != Uri.UriSchemeHttps || !url.Host.EndsWith("nvidia.com", StringComparison.OrdinalIgnoreCase)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", url.ToString()) { UseShellExecute = true });
    }
}

public sealed class GpuDriverItemViewModel(GpuDriverStatus status, DateTime? installedDate)
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

    public string Message => Status.Message;

    public string Installed => $"Installé : {Status.InstalledVersion ?? "?"}" +
                               (installedDate is { } d ? $" du {d.ToString("d MMMM yyyy", Fr)}" : "");

    public bool HasLatest => Status.Latest is not null;

    public string Latest => Status.Latest is { } l
        ? $"Dernier publié par NVIDIA : {l.Version}" + (l.ReleaseDate is { } r ? $" du {r.ToString("d MMMM yyyy", Fr)}" : "") +
          (l.SizeText is { } size ? $" ({size.Replace("MB", "Mo").Replace('.', ',')})" : "")
        : "";

    public Uri? DetailsUrl => Status.Latest?.DetailsUrl;

    public bool HasDetails => DetailsUrl is not null;
}

public sealed class WindowsUpdateDriverItemViewModel(WindowsUpdateDriver update)
{
    public WindowsUpdateDriver Update { get; } = update;

    public string Title => Update.Title;

    public string Details => string.Join(" · ", new[]
    {
        Update.DriverClass is { } c ? $"Catégorie : {c}" : null,
        Update.DriverDate is { } d ? $"du {d:dd/MM/yyyy}" : null,
        Update.SizeBytes is > 0 and var size ? $"{size / 1048576.0:N1} Mo" : null,
        Update.MayRequireReboot ? "redémarrage possible" : null,
    }.OfType<string>());
}
