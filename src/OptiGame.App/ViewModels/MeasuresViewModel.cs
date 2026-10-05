using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Logging;
using OptiGame.Core.Measurement;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;
using OptiGame.Platform.Measurement;

namespace OptiGame.App.ViewModels;

/// <summary>
/// Mesures PresentMon : lancement, liste (manuelles et automatiques, ajoutées dès qu'elles existent), statistiques et
/// comparaison avant / après. Le chemin de PresentMon se règle dans Paramètres (et ici quand il manque).
/// </summary>
public sealed partial class MeasuresViewModel : ObservableObject
{
    private readonly CaptureStore _store;
    private readonly PresentMonRunner _runner;
    private readonly AppSettingsStore _settings;
    private readonly ProfileStore _profiles;
    private readonly GameSessionManager _sessions;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _time;
    private readonly FileLog _log;
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _cancellation;
    private DateTimeOffset _recordingStartsAt;
    private DateTimeOffset _recordingEndsAt;

    public MeasuresViewModel(CaptureStore store, PresentMonRunner runner, AppSettingsStore settings, ProfileStore profiles,
        GameSessionManager sessions, IDialogService dialogs, TimeProvider time, FileLog log, AutoCapture autoCapture)
    {
        // Mesure automatique terminée (pendant une partie) : ajoutée à la liste tout de suite, sélection gardée.
        autoCapture.CaptureAdded += (_, _) => OnUi(() => RefreshCaptures(keepSelection: true));
        // Partie en cours : c'est le jeu à mesurer.
        sessions.SessionStarted += (_, _) => OnUi(SelectCurrentGame);
        _store = store;
        _runner = runner;
        _settings = settings;
        _profiles = profiles;
        _sessions = sessions;
        _dialogs = dialogs;
        _time = time;
        _log = log;
        _countdown.Tick += (_, _) => UpdateCountdown();

        // PresentMon : chemin configuré, sinon celui du dossier des outils d'OptiGame (mémorisé).
        PresentMonPath = settings.Get().PresentMonPath;
        if (PresentMonPath is null && runner.FindInToolsDirectory() is { } found)
        {
            SetPresentMonPath(found);
        }

        profiles.Changed += (_, _) => OnUi(RefreshTargets);
        RefreshTargets();
        SelectCurrentGame();
        RefreshCaptures();
    }

    private static void OnUi(Action action) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);

    private void SelectCurrentGame()
    {
        if (_sessions.Current is { } current && !IsCapturing) SelectTarget(Path.GetFileName(current.Profile.ExePath));
    }

    /// <summary>La liste a été rechargée : la vue doit refléter la sélection (Before/After).</summary>
    public event EventHandler? SelectionReset;

    public ObservableCollection<CaptureTarget> Targets { get; } = [];

    public ObservableCollection<CaptureItemViewModel> Captures { get; } = [];

    public IReadOnlyList<int> DurationOptions { get; } = [30, 60, 120, 300];

    public IReadOnlyList<int> DelayOptions { get; } = [0, 5, 10, 15];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPresentMon))]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    private string? _presentMonPath;

    public bool HasPresentMon => PresentMonPath is not null && File.Exists(PresentMonPath);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    private CaptureTarget? _selectedTarget;

    /// <summary>Nom d'exe saisi à la main (prioritaire sur la liste).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    private string _customExeName = "";

    [ObservableProperty] private int _selectedDuration = 60;
    [ObservableProperty] private int _selectedDelay = 10;
    [ObservableProperty] private string _label = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCaptureCommand))]
    private bool _isCapturing;

    /// <summary>Statut de la mesure en cours ou de la dernière (InfoBar).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCaptureStatus))]
    private string _captureStatus = "";

    [ObservableProperty] private Controls.Severity _captureStatusSeverity = Controls.Severity.Info;

    public bool HasCaptureStatus => CaptureStatus.Length > 0;

    /// <summary>Avancement de la mesure en cours (délai puis enregistrement), de 0 à 100.</summary>
    [ObservableProperty] private double _captureProgress;

    [RelayCommand]
    private void DismissCaptureStatus() => CaptureStatus = "";

    public bool HasCaptures => Captures.Count > 0;

    /// <summary>Plus de deux mesures sélectionnées : seules les deux premières sont comparées (dit, pas ignoré en silence).</summary>
    [ObservableProperty] private string _selectionNote = "";

    /// <summary>« Inverser » : l'utilisateur choisit laquelle est « avant » (par défaut, la plus ancienne).</summary>
    private bool _swapped;

    /// <summary>Résumé du graphe pour les lecteurs d'écran (le graphe lui-même n'est pas lisible).</summary>
    [ObservableProperty] private string _chartDescription = "Graphe des temps d'image : aucune mesure sélectionnée.";

    // Sélection (1 ou 2 captures) : statistiques, comparaison et graphe.
    [ObservableProperty] private CaptureItemViewModel? _before;
    [ObservableProperty] private CaptureItemViewModel? _after;
    [ObservableProperty] private IReadOnlyList<double>? _beforeFrames;
    [ObservableProperty] private IReadOnlyList<double>? _afterFrames;
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _comparison = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private bool _hasSelection;
    [ObservableProperty] private bool _isComparison;

    private string? TargetExe =>
        !string.IsNullOrWhiteSpace(CustomExeName)
            ? (CustomExeName.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? CustomExeName.Trim() : CustomExeName.Trim() + ".exe")
            : SelectedTarget?.ExeName;

    private bool CanStart() => !IsCapturing && HasPresentMon && TargetExe is not null;

    [RelayCommand]
    private void ChoosePresentMon()
    {
        if (_dialogs.PickProgram("Choisir PresentMon (version console, ex. PresentMon-2.6.0-x64.exe)", PresentMonPath) is { } path)
        {
            SetPresentMonPath(path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartCaptureAsync()
    {
        var exe = TargetExe!;
        var now = _time.GetLocalNow();
        var label = string.IsNullOrWhiteSpace(Label) ? $"{Path.GetFileNameWithoutExtension(exe)} — {Core.Text.FrenchText.DateAndTime(now.DateTime)}" : Label.Trim();
        var csvName = $"{now:yyyyMMdd-HHmmss}_{Path.GetFileNameWithoutExtension(exe)}.csv";
        var request = new CaptureRequest(PresentMonPath!, exe, SelectedDuration, SelectedDelay, Path.Combine(_store.Directory, csvName));

        IsCapturing = true;
        _cancellation = new CancellationTokenSource();
        _recordingStartsAt = now.AddSeconds(SelectedDelay);
        _recordingEndsAt = _recordingStartsAt.AddSeconds(SelectedDuration);
        UpdateCountdown();
        _countdown.Start();
        try
        {
            var output = await _runner.RunAsync(request, _cancellation.Token);
            var record = await Task.Run(() => BuildRecord(request, label, csvName, now, output));
            _store.Add(record);
            RefreshCaptures(select: record.Id);
            Label = "";
            ShowStatus(Controls.Severity.Success, $"Mesure terminée : {record.Stats.AverageFps:0} FPS moyens, 1 % low : {record.Stats.OnePercentLowFps:0} FPS.");
        }
        catch (OperationCanceledException)
        {
            File.Delete(request.OutputCsv); // mesure partielle, sans intérêt
            ShowStatus(Controls.Severity.Info, "Mesure annulée : rien n'a été enregistré.");
        }
        catch (FormatException ex)
        {
            // Fichier produit mais non reconnu : on le GARDE pour pouvoir l'analyser.
            var kept = Path.ChangeExtension(request.OutputCsv, null) + "-non-reconnu.csv";
            File.Move(request.OutputCsv, kept, overwrite: true);
            ShowStatus(Controls.Severity.Error, "Mesure non reconnue (fichier conservé pour analyse).");
            _log.Error($"CSV PresentMon non reconnu, conservé dans {kept}", ex);
            _dialogs.ShowError("Mesure non reconnue",
                $"PresentMon a produit un fichier qu'OptiGame ne sait pas lire. Il est conservé pour analyse :\n{kept}", ex.Message);
        }
        catch (Exception ex)
        {
            ShowStatus(Controls.Severity.Error, "Mesure échouée : rien n'a été enregistré.");
            _log.Error("Capture échouée", ex);
            _dialogs.ShowError("Mesure échouée",
                ex is FileNotFoundException
                    ? "PresentMon est introuvable : choisissez de nouveau son emplacement, puis réessayez."
                    : "La mesure n'a pas abouti. Vérifiez que le jeu tourne au premier plan, puis réessayez.",
                ex.Message);
        }
        finally
        {
            _countdown.Stop();
            _cancellation.Dispose();
            _cancellation = null;
            CaptureProgress = 0;
            IsCapturing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsCapturing))]
    private void CancelCapture() => _cancellation?.Cancel();

    /// <summary>« Supprimer… » (bouton, touche Suppr, menu) : seulement avec une sélection.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected()
    {
        var selected = new[] { Before, After }.OfType<CaptureItemViewModel>().ToList();
        if (selected.Count == 0) return;
        if (!_dialogs.Confirm(selected.Count == 1 ? "Supprimer cette mesure ?" : "Supprimer ces 2 mesures ?",
                selected.Count == 1
                    ? "Son résultat et son fichier CSV sont effacés définitivement."
                    : "Leurs résultats et leurs fichiers CSV sont effacés définitivement.",
                "Supprimer", isDestructive: true)) return;
        foreach (var item in selected) _store.Remove(item.Record.Id);
        RefreshCaptures();
    }

    /// <summary>Présélectionne un jeu (bouton « Mesurer les FPS » de la page d'un jeu).</summary>
    public void SelectTarget(string exeName)
    {
        RefreshTargets();
        if (Targets.FirstOrDefault(t => t.ExeName.Equals(exeName, StringComparison.OrdinalIgnoreCase)) is { } target)
        {
            SelectedTarget = target;
            CustomExeName = "";
        }
        else
        {
            CustomExeName = exeName;
        }
    }

    /// <summary>Appelé par la vue quand la sélection de la liste change.</summary>
    public void UpdateSelection(IReadOnlyList<CaptureItemViewModel> selected)
    {
        _selected = selected.Take(2).ToList();
        SelectionNote = selected.Count > 2 ? "Seules les 2 premières mesures sélectionnées sont comparées." : "";
        _swapped = false;
        ApplySelection();
    }

    private IReadOnlyList<CaptureItemViewModel> _selected = [];

    /// <summary>Avant = la plus ancienne des deux (ou l'inverse après « Inverser »).</summary>
    private void ApplySelection()
    {
        var ordered = _selected.OrderBy(c => c.Record.CapturedAt).ToList();
        if (_swapped) ordered.Reverse();
        Before = ordered.ElementAtOrDefault(0);
        After = ordered.ElementAtOrDefault(1);
        HasSelection = Before is not null;
        IsComparison = After is not null;
        Comparison = Before is not null && After is not null ? ComparisonRow.Build(Before.Record.Stats, After.Record.Stats) : [];
        BeforeFrames = LoadFrames(Before);
        AfterFrames = LoadFrames(After);
        ChartDescription = Before is null ? "Graphe des temps d'image : aucune mesure sélectionnée."
            : After is null ? $"Graphe des temps d'image de « {Before.Label} » : {Before.AverageFps} FPS moyens, pire image {Before.Record.Stats.MaxFrameTimeMs:0.0} ms."
            : $"Graphe des temps d'image : avant {Before.AverageFps} FPS moyens, après {After.AverageFps} FPS moyens.";
    }

    [RelayCommand]
    private void Swap()
    {
        _swapped = !_swapped;
        ApplySelection();
    }

    private CaptureRecord BuildRecord(CaptureRequest request, string label, string csvName, DateTimeOffset capturedAt, string presentMonOutput)
    {
        var session = _sessions.Current;
        var activeProfile = session is not null &&
                            Path.GetFileName(session.Profile.ExePath).Equals(request.ProcessName, StringComparison.OrdinalIgnoreCase)
            ? session.Profile.Name
            : null;
        var preset = _profiles.GetAll()
            .FirstOrDefault(p => Path.GetFileName(p.ExePath).Equals(request.ProcessName, StringComparison.OrdinalIgnoreCase))?.GraphicsPreset;
        return Platform.Measurement.CaptureReader.Build(request, label, csvName, capturedAt, presentMonOutput, activeProfile, preset: preset);
    }

    private IReadOnlyList<double>? LoadFrames(CaptureItemViewModel? item)
    {
        if (item is null) return null;
        try
        {
            return _store.LoadFrameTimes(item.Record);
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            _log.Warn($"CSV de la capture « {item.Record.Label} » illisible : {ex.Message}");
            return null;
        }
    }

    private void UpdateCountdown()
    {
        var now = _time.GetLocalNow();
        CaptureStatusSeverity = Controls.Severity.Info;
        CaptureStatus = now < _recordingStartsAt
            ? $"Démarrage dans {(int)Math.Ceiling((_recordingStartsAt - now).TotalSeconds)} s : passez dans le jeu et jouez normalement."
            : $"Mesure en cours : encore {Math.Max(0, (int)Math.Ceiling((_recordingEndsAt - now).TotalSeconds))} s…";
        var total = (_recordingEndsAt - _recordingStartsAt).TotalSeconds + SelectedDelay;
        var elapsed = total - (_recordingEndsAt - now).TotalSeconds;
        CaptureProgress = total > 0 ? Math.Clamp(elapsed / total * 100, 0, 100) : 0;
    }

    private void ShowStatus(Controls.Severity severity, string text)
    {
        CaptureStatusSeverity = severity;
        CaptureStatus = text;
    }

    private void SetPresentMonPath(string path)
    {
        PresentMonPath = path;
        _settings.Update(s => s.PresentMonPath = path);
    }

    private void RefreshTargets()
    {
        var previous = SelectedTarget?.ExeName;
        Targets.Clear();
        foreach (var profile in _profiles.GetAll().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Targets.Add(new CaptureTarget(profile.Name, Path.GetFileName(profile.ExePath)));
        }
        SelectedTarget = Targets.FirstOrDefault(t => t.ExeName == previous) ?? Targets.FirstOrDefault();
    }

    private void RefreshCaptures(Guid? select = null, bool keepSelection = false)
    {
        var kept = keepSelection ? _selected.Select(c => c.Record.Id).ToHashSet() : [];
        Captures.Clear();
        foreach (var record in _store.GetAll())
        {
            Captures.Add(new CaptureItemViewModel(record, _time, record.Id == select || kept.Contains(record.Id)));
        }
        OnPropertyChanged(nameof(HasCaptures));
        UpdateSelection(Captures.Where(c => c.IsInitiallySelected).ToList());
        SelectionReset?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record CaptureTarget(string Name, string ExeName)
{
    public override string ToString() => $"{Name} ({ExeName})";
}

public sealed class CaptureItemViewModel(CaptureRecord record, TimeProvider time, bool isInitiallySelected)
{
    public CaptureRecord Record { get; } = record;

    public bool IsInitiallySelected { get; } = isInitiallySelected;

    public string Label => Record.Label;

    public string Game => Record.ProcessName;

    public string Date => Core.Text.FrenchText.DateAndTime(TimeZoneInfo.ConvertTime(Record.CapturedAt, time.LocalTimeZone).DateTime);

    public string Profile => Record.ActiveProfile ?? "—";

    public bool IsAutomatic => Record.Automatic;

    public bool IsUnreliable => !Record.Stats.IsReliable;

    /// <summary>« portal2.exe · 4 oct. 2026, 15:21 · optimisé » sous le libellé.</summary>
    public string Details => string.Join(" · ", new[]
    {
        Game,
        Date,
        Record.ActiveProfile is null ? "sans optimisation" : "optimisé",
    });

    public override string ToString() =>
        $"{Label}, {Game}, {Date}, {AverageFps} FPS moyens, 1 % low {OnePercentLow}" +
        (IsAutomatic ? ", mesure automatique" : "") + (IsUnreliable ? ", peu fiable" : "");

    public string AverageFps => $"{Record.Stats.AverageFps:0.0}";

    public string OnePercentLow => $"{Record.Stats.OnePercentLowFps:0.0}";

    public string PointOnePercentLow => $"{Record.Stats.PointOnePercentLowFps:0.0}";

    public string P99 => $"{Record.Stats.P99FrameTimeMs:0.00} ms";

    public string Summary =>
        $"{Record.Stats.FrameCount} images sur {Record.Stats.DurationSeconds:0} s — médiane {Record.Stats.MedianFrameTimeMs:0.00} ms, " +
        $"pire image {Record.Stats.MaxFrameTimeMs:0.0} ms" +
        (Record.Stats.IsReliable ? "" : $" — moins de {FrameStats.MinimumFrames} images : 1 % low peu fiable");
}

/// <summary>Ligne du tableau de comparaison ; <see cref="IsBetter"/> tient compte du sens de la mesure.</summary>
public sealed record ComparisonRow(string Metric, string Before, string After, string Change, bool? IsBetter)
{
    /// <summary>L'écart en mot (la couleur ne porte jamais le sens seule) : mieux, moins bien, stable (moins de 1 %).</summary>
    public string Verdict => IsBetter switch { true => "mieux", false => "moins bien", _ => "stable" };

    /// <summary>Flèche : haut = mieux, bas = moins bien (même pour P99, où « mieux » = plus bas), tiret = stable.</summary>
    public string Glyph => IsBetter switch { true => "", false => "", _ => "" };

    public static IReadOnlyList<ComparisonRow> Build(FrameStats before, FrameStats after)
    {
        var c = new FrameStatsComparison(before, after);
        return
        [
            Row("FPS moyens", before.AverageFps, after.AverageFps, "0.0", c.AverageFpsChangePercent, higherIsBetter: true),
            Row("1 % low (FPS)", before.OnePercentLowFps, after.OnePercentLowFps, "0.0", c.OnePercentLowChangePercent, higherIsBetter: true),
            Row("0,1 % low (FPS)", before.PointOnePercentLowFps, after.PointOnePercentLowFps, "0.0", c.PointOnePercentLowChangePercent, higherIsBetter: true),
            Row("Temps d'image P99 (ms)", before.P99FrameTimeMs, after.P99FrameTimeMs, "0.00", c.P99FrameTimeChangePercent, higherIsBetter: false),
        ];
    }

    private static ComparisonRow Row(string metric, double before, double after, string format, double change, bool higherIsBetter) =>
        new(metric, before.ToString(format), after.ToString(format), $"{change:+0.0;-0.0;0.0} %",
            Math.Abs(change) < 1 ? null : (change > 0) == higherIsBetter);
}
