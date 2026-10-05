using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OptiGame.App.Services;
using OptiGame.Core.Diagnostics;
using OptiGame.Core.State;
using OptiGame.Core.Text;
using OptiGame.Platform;

namespace OptiGame.App.ViewModels;

/// <summary>
/// Diagnostic, en deux vues. « Simple » : un bouton qui applique les corrections recommandées (une confirmation qui les liste
/// toutes) et un bouton qui annule celles du Diagnostic (<see cref="OneClickOptimization"/>). « Avancé » : chaque contrôle et
/// chaque correction à la main ; les points à corriger en grandes cartes, le reste en lignes repliables.
/// </summary>
public sealed partial class DiagnosticViewModel(
    DiagnosticRunner runner,
    [FromKeyedServices(JournalKeys.Fixes)] ChangeJournal fixes,
    IDialogService dialogs,
    TimeProvider time,
    NavigationService navigation,
    DriversViewModel drivers,
    Core.Settings.AppSettingsStore settings) : ObservableObject
{
    // ---- Vue Simple / Avancé (mémorisée) ----

    public bool IsAdvanced
    {
        get => settings.Get().DiagnosticAdvanced;
        set
        {
            if (value == IsAdvanced) return;
            settings.Update(s => s.DiagnosticAdvanced = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSimple));
        }
    }

    public bool IsSimple
    {
        get => !IsAdvanced;
        set => IsAdvanced = !value;
    }

    [RelayCommand]
    private void ShowAdvanced() => IsAdvanced = true;

    // ---- Vue Simple ----

    private OptimizationOverview? _overview;

    [ObservableProperty] private string _headline = "Analyse en cours…";
    [ObservableProperty] private string _countsText = "";
    [ObservableProperty] private bool _isOptimized;
    [ObservableProperty] private IReadOnlyList<string> _recommendedTitles = [];
    [ObservableProperty] private IReadOnlyList<string> _appliedTitles = [];
    [ObservableProperty] private string _activateLabel = "Activer les optimisations";
    [ObservableProperty] private string _deactivateLabel = "Désactiver les optimisations";
    [ObservableProperty] private string _optionalText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivateAllCommand))]
    private bool _hasRecommended;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeactivateAllCommand))]
    private bool _hasDiagnosticFixesApplied;

    [ObservableProperty] private bool _hasOptional;

    /// <summary>Points à corriger soi-même (BIOS, pilote, option avancée) : rappelés dans la vue Simple.</summary>
    public ObservableCollection<DiagnosticItemViewModel> ManualItems { get; } = [];

    [ObservableProperty] private bool _hasManualItems;

    private bool CanActivateAll() => !IsBusy && HasRecommended;

    /// <summary>Applique les corrections recommandées, une par une dans le journal (annulables séparément), après UNE confirmation.</summary>
    [RelayCommand(CanExecute = nameof(CanActivateAll))]
    private async Task ActivateAllAsync()
    {
        if (_overview is not { } overview || !dialogs.ConfirmChanges(overview.Recommended)) return;

        IsBusy = true;
        var failed = new List<(string Title, string Error)>();
        try
        {
            foreach (var change in overview.Recommended)
            {
                try
                {
                    await Task.Run(() => fixes.Apply(change));
                }
                catch (Exception ex)
                {
                    failed.Add((change.Title, ex.Message));
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        var applied = overview.Recommended.Count - failed.Count;
        if (failed.Count > 0)
        {
            dialogs.ShowError(
                $"{FrenchText.Count(failed.Count, "optimisation n'a", "optimisations n'ont")} pas pu être {FrenchText.Agree(failed.Count, "appliquée", "appliquées")}",
                string.Join("\n", failed.Select(f => $"• {f.Title}")) + "\n\nRien n'a été modifié pour " +
                FrenchText.Agree(failed.Count, "elle", "elles") + "." +
                (applied > 0 ? $" Les autres ({applied}) sont actives." : ""),
                string.Join("\n", failed.Select(f => $"{f.Title} : {f.Error}")));
        }
        else if (overview.Recommended.Any(c => c.RequiresReboot))
        {
            dialogs.ShowInfo($"{FrenchText.Count(applied, "optimisation appliquée", "optimisations appliquées")}",
                "Redémarrez Windows pour que toutes soient prises en compte.");
        }
        await RunAsync();
    }

    private bool CanDeactivateAll() => !IsBusy && HasDiagnosticFixesApplied;

    /// <summary>Annule toutes les corrections du Diagnostic (jamais les réglages par jeu, qui sont dans le même journal).</summary>
    [RelayCommand(CanExecute = nameof(CanDeactivateAll))]
    private async Task DeactivateAllAsync()
    {
        if (_overview is not { } overview || overview.Applied.Count == 0) return;
        var count = overview.Applied.Count;
        if (!dialogs.Confirm(
                count == 1 ? "Restaurer le réglage d'origine ?" : $"Restaurer les {count} réglages d'origine ?",
                string.Join("\n", overview.Applied.Select(c => $"• {c.Title}")) +
                "\n\nOptiGame rétablit les réglages sauvegardés avant ces optimisations. Les réglages propres à chaque jeu " +
                "(plafond de FPS, carte graphique) ne sont pas concernés." +
                (overview.Applied.Any(c => c.RequiresReboot) ? "\n\nRedémarrez ensuite Windows pour que tout soit pris en compte." : ""),
                "Tout restaurer"))
        {
            return;
        }

        IsBusy = true;
        var failed = new List<(string Title, string Detail)>();
        try
        {
            foreach (var change in overview.Applied)
            {
                var report = await Task.Run(() => fixes.Undo(change.Id));
                failed.AddRange(report.Failed.Select(f => (change.Title, $"{f.Target} : {f.Error}")));
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (failed.Count > 0)
        {
            var titles = failed.Select(f => f.Title).Distinct().ToList();
            dialogs.ShowError("Restauration incomplète",
                "Ces réglages n'ont pas pu être rétablis :\n" + string.Join("\n", titles.Select(t => $"• {t}")) +
                "\n\nLeurs optimisations restent actives et listées : vous pourrez réessayer.",
                string.Join("\n", failed.Select(f => $"{f.Title} — {f.Detail}")));
        }
        await RunAsync();
    }

    // ---- Vue Avancé : contrôles regroupés par statut ----

    public ObservableCollection<DiagnosticItemViewModel> AttentionItems { get; } = [];

    public ObservableCollection<DiagnosticItemViewModel> InfoItems { get; } = [];

    public ObservableCollection<DiagnosticItemViewModel> OkItems { get; } = [];

    /// <summary>Une analyse a-t-elle déjà été faite (la fenêtre la lance à sa première ouverture) ?</summary>
    public bool HasResults => AttentionItems.Count + InfoItems.Count + OkItems.Count > 0;

    [ObservableProperty] private string _attentionHeader = "";
    [ObservableProperty] private string _infoHeader = "";
    [ObservableProperty] private string _okHeader = "";

    /// <summary>Lien d'un contrôle vers une autre page (ex. « Rechercher les mises à jour de pilotes »).</summary>
    [RelayCommand]
    private void OpenLink(DiagnosticLinkTarget target)
    {
        switch (target)
        {
            case DiagnosticLinkTarget.Drivers:
                navigation.Navigate(drivers);
                break;
        }
    }

    /// <summary>Corrections appliquées par OptiGame et encore actives (annulables).</summary>
    public ObservableCollection<AppliedFixViewModel> AppliedFixes { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(ActivateAllCommand), nameof(DeactivateAllCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Le diagnostic lit la configuration sans rien modifier.";

    [ObservableProperty]
    private bool _hasAppliedFixes;

    /// <summary>Contrôles « À corriger » ou qui n'ont pas pu s'exécuter : badge de la page dans la navigation.</summary>
    [ObservableProperty]
    private int _problemCount;

    private bool CanRun() => !IsBusy;

    /// <summary>Lecture seule : aucun réglage n'est modifié.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        IsBusy = true;
        StatusText = "Analyse en cours…";
        try
        {
            // WMI et P/Invoke hors du thread UI.
            var results = await Task.Run(runner.RunAll);
            AttentionItems.Clear();
            InfoItems.Clear();
            OkItems.Clear();
            foreach (var result in results)
            {
                var fixVms = result.Fixes
                    .Where(f => !fixes.IsActive(f.Change.Id))
                    .Select(f => new FixViewModel(f, ApplyFixAsync))
                    .ToList();
                var item = new DiagnosticItemViewModel(result, fixVms);
                (result.Status switch
                {
                    DiagnosticStatus.NeedsAttention => AttentionItems,
                    DiagnosticStatus.Ok => OkItems,
                    _ => InfoItems,
                }).Add(item);
            }
            AttentionHeader = $"À CORRIGER ({AttentionItems.Count})";
            InfoHeader = $"À SAVOIR ({InfoItems.Count})";
            OkHeader = $"OK ({OkItems.Count})";

            RefreshAppliedFixes();
            RefreshOverview(results);
            var attention = results.Count(r => r.Status == DiagnosticStatus.NeedsAttention);
            ProblemCount = attention + results.Count(r => r.Status == DiagnosticStatus.Error);
            StatusText = $"Dernière analyse : {time.GetLocalNow():HH:mm:ss} — " +
                (attention == 0 ? "rien à corriger." : $"{attention} point(s) à corriger.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyFixAsync(DiagnosticFix fix)
    {
        if (IsBusy || !dialogs.ConfirmChange(fix.Change, fix.IsAdvanced))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => fixes.Apply(fix.Change));
        }
        catch (Exception ex)
        {
            dialogs.ShowError("Optimisation non appliquée", $"« {fix.Change.Title} » n'a pas pu être appliquée : rien n'a été modifié.", ex.Message);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (fix.Change.RequiresReboot)
        {
            dialogs.ShowInfo("Optimisation appliquée", $"« {fix.Change.Title} » sera prise en compte au prochain redémarrage de Windows.");
        }
        await RunAsync();
    }

    private async Task UndoAsync(ChangeRecord change)
    {
        if (IsBusy || !dialogs.ConfirmUndo(change))
        {
            return;
        }

        IsBusy = true;
        RestoreReport report;
        try
        {
            report = await Task.Run(() => fixes.Undo(change.Id));
        }
        finally
        {
            IsBusy = false;
        }

        if (!report.Success)
        {
            dialogs.ShowError("Restauration impossible",
                $"Le réglage d'origine de « {change.Title} » n'a pas pu être rétabli. L'optimisation reste active et listée : vous pourrez réessayer.",
                string.Join("\n", report.Failed.Select(f => $"{f.Target} : {f.Error}")));
        }
        else if (report.ModifiedExternally.Count > 0)
        {
            dialogs.ShowInfo("Réglage d'origine restauré",
                "Il avait été modifié entre-temps par un autre programme qu'OptiGame : c'est la valeur sauvegardée avant l'optimisation qui est revenue." +
                (change.RequiresReboot ? "\n\nRedémarrez Windows pour qu'elle soit prise en compte." : ""));
        }
        else if (change.RequiresReboot)
        {
            dialogs.ShowInfo("Réglage d'origine restauré", "Redémarrez Windows pour qu'il soit pris en compte.");
        }

        await RunAsync();
    }

    private void RefreshOverview(IReadOnlyList<DiagnosticResult> results)
    {
        var overview = _overview = OneClickOptimization.Overview(results, fixes.ActiveChanges);
        IsOptimized = overview.IsOptimized;
        Headline = overview.Recommended.Count > 0
            ? overview.Recommended.Count == 1 ? "1 optimisation recommandée" : $"{overview.Recommended.Count} optimisations recommandées"
            : overview.ManualActions.Count > 0 ? "Optimisé par OptiGame, il reste à faire de votre côté"
            : "Votre PC est prêt pour le jeu";
        CountsText = $"{results.Count} contrôles : {overview.OkCount} OK · {overview.AttentionCount} à corriger · {overview.InfoCount} à savoir";
        RecommendedTitles = overview.Recommended.Select(c => c.Title).ToList();
        AppliedTitles = overview.Applied.Select(c => c.Title).ToList();
        HasRecommended = overview.Recommended.Count > 0;
        HasDiagnosticFixesApplied = overview.Applied.Count > 0;
        ActivateLabel = overview.Recommended.Count > 0 ? $"Activer les optimisations ({overview.Recommended.Count})" : "Optimisations activées";
        DeactivateLabel = $"Désactiver les optimisations ({overview.Applied.Count})";
        HasOptional = overview.Optional.Count > 0;
        OptionalText = overview.Optional.Count == 1
            ? $"1 réglage facultatif dans la vue Avancé : {overview.Optional[0].Title}."
            : $"{overview.Optional.Count} réglages facultatifs dans la vue Avancé : {string.Join(", ", overview.Optional.Select(r => r.Title))}.";

        ManualItems.Clear();
        foreach (var manual in overview.ManualActions)
        {
            ManualItems.Add(new DiagnosticItemViewModel(manual, []));
        }
        HasManualItems = ManualItems.Count > 0;
    }

    private void RefreshAppliedFixes()
    {
        AppliedFixes.Clear();
        foreach (var change in fixes.ActiveChanges)
        {
            AppliedFixes.Add(new AppliedFixViewModel(change, time, UndoAsync));
        }
        HasAppliedFixes = AppliedFixes.Count > 0;
    }
}

public sealed class DiagnosticItemViewModel(DiagnosticResult result, IReadOnlyList<FixViewModel> fixes)
{
    public DiagnosticResult Result { get; } = result;

    public string Title => Result.IsEstimate ? $"{Result.Title} (estimation)" : Result.Title;

    public DiagnosticStatus Status => Result.Status;

    public string StatusLabel => Result.Status switch
    {
        DiagnosticStatus.Ok => "OK",
        DiagnosticStatus.NeedsAttention => "À corriger",
        DiagnosticStatus.Info => "Info",
        _ => "Erreur",
    };

    public string Summary => Result.Summary;

    public string Explanation => Result.Explanation;

    public bool HasExplanation => !string.IsNullOrWhiteSpace(Result.Explanation);

    public IReadOnlyList<string> Details => Result.Details;

    public IReadOnlyList<FixViewModel> Fixes { get; } = fixes.Where(f => !f.IsAdvanced).ToList();

    public IReadOnlyList<FixViewModel> AdvancedFixes { get; } = fixes.Where(f => f.IsAdvanced).ToList();

    public bool HasAdvancedFixes => AdvancedFixes.Count > 0;

    public bool HasLink => Result.Link is not null;

    public string LinkLabel => Result.Link?.Label ?? "";

    public DiagnosticLinkTarget LinkTarget => Result.Link?.Target ?? default;
}

public sealed partial class FixViewModel(DiagnosticFix fix, Func<DiagnosticFix, Task> apply)
{
    public string Title => fix.Change.Title;

    public bool IsAdvanced => fix.IsAdvanced;

    public string Badges => string.Join(" · ", new[]
    {
        fix.Change.RequiresAdmin ? "admin" : null,
        fix.Change.RequiresReboot ? "redémarrage" : null,
    }.OfType<string>());

    [RelayCommand]
    private Task ApplyAsync() => apply(fix);
}

public sealed partial class AppliedFixViewModel(ChangeRecord change, TimeProvider time, Func<ChangeRecord, Task> undo)
{
    public string Title => change.Title;

    public string What => change.What;

    public string AppliedAt =>
        $"Appliquée le {TimeZoneInfo.ConvertTime(change.AppliedAt, time.LocalTimeZone):dd/MM/yyyy à HH:mm}";

    [RelayCommand]
    private Task UndoAsync() => undo(change);
}
