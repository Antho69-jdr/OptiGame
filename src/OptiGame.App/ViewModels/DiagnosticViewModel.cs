using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OptiGame.App.Controls;
using OptiGame.App.Services;
using OptiGame.Core.Diagnostics;
using OptiGame.Core.Profiles;
using OptiGame.Core.State;
using OptiGame.Core.Text;
using OptiGame.Platform;

namespace OptiGame.App.ViewModels;

/// <summary>
/// Diagnostic, sur une seule page : en haut le verdict et l'action principale (« Appliquer les N optimisations… », une
/// confirmation qui les liste toutes : <see cref="OneClickOptimization"/>), puis les optimisations actives (restaurables une à
/// une ou toutes), puis chaque contrôle par statut : À corriger, Non vérifié, À savoir, OK. Lecture seule tant que
/// l'utilisateur ne confirme rien.
/// </summary>
public sealed partial class DiagnosticViewModel(
    DiagnosticRunner runner,
    [FromKeyedServices(JournalKeys.Fixes)] ChangeJournal fixes,
    IDialogService dialogs,
    TimeProvider time,
    NavigationService navigation,
    DriversViewModel drivers) : ObservableObject
{
    private OptimizationOverview? _overview;

    /// <summary>Contrôles dépliés (clé = CheckId), gardés d'une analyse à l'autre.</summary>
    private readonly HashSet<string> _expanded = [];

    // ---- Verdict et action principale ----

    /// <summary>Null tant que la première analyse n'est pas finie : en-tête neutre (ni vert ni orange avant de savoir).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVerdict))]
    private DiagnosticVerdict? _verdict;

    public bool HasVerdict => Verdict is not null;

    [ObservableProperty] private string _headline = "Analyse en cours…";
    [ObservableProperty] private string _countsText = "";
    [ObservableProperty] private string _verdictDetail = "Le diagnostic lit la configuration de Windows sans rien modifier.";
    [ObservableProperty] private IReadOnlyList<string> _recommendedTitles = [];
    [ObservableProperty] private string _applyAllLabel = "Appliquer les optimisations…";
    [ObservableProperty] private string _optionalText = "";
    [ObservableProperty] private bool _hasOptional;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyAllCommand))]
    private bool _hasRecommended;

    /// <summary>Bilan de la dernière action (InfoBar fermable) : succès, redémarrage à prévoir.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutcome))]
    private string _outcomeTitle = "";

    [ObservableProperty] private string _outcomeMessage = "";
    [ObservableProperty] private Severity _outcomeSeverity = Severity.Success;

    public bool HasOutcome => OutcomeTitle.Length > 0;

    [RelayCommand]
    private void DismissOutcome() => OutcomeTitle = "";

    private bool CanApplyAll() => !IsBusy && HasRecommended;

    /// <summary>Applique les optimisations recommandées, une par une dans le journal (restaurables séparément), après UNE confirmation.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyAll))]
    private async Task ApplyAllAsync()
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
        if (applied > 0)
        {
            var reboot = overview.Recommended.Where(c => !failed.Any(f => f.Title == c.Title)).Any(c => c.RequiresReboot);
            ShowOutcome(reboot ? Severity.Warning : Severity.Success,
                $"{FrenchText.Count(applied, "optimisation appliquée", "optimisations appliquées")}" + (failed.Count > 0 ? $", {failed.Count} en échec" : ""),
                reboot ? "Redémarrez Windows pour que toutes soient prises en compte." : "Chacune reste restaurable ci-dessous.");
        }
        await RunAsync();
    }

    // ---- Optimisations actives ----

    /// <summary>Optimisations du Diagnostic actives (« fix. »).</summary>
    public ObservableCollection<AppliedFixViewModel> ActiveOptimizations { get; } = [];

    /// <summary>Réglages faits pour un jeu (« game. »), listés à part : ils se gèrent depuis la fiche du jeu.</summary>
    public ObservableCollection<AppliedFixViewModel> GameOptimizations { get; } = [];

    [ObservableProperty] private bool _hasActiveOptimizations;
    [ObservableProperty] private bool _hasGameOptimizations;
    [ObservableProperty] private string _activeHeader = "";
    [ObservableProperty] private string _gameHeader = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreAllCommand))]
    private bool _canRestoreAllOptimizations;

    private bool CanRestoreAll() => !IsBusy && CanRestoreAllOptimizations;

    /// <summary>« Tout restaurer… » : les optimisations du Diagnostic seulement, jamais les réglages par jeu.</summary>
    [RelayCommand(CanExecute = nameof(CanRestoreAll))]
    private async Task RestoreAllAsync()
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
        else
        {
            var reboot = overview.Applied.Any(c => c.RequiresReboot);
            ShowOutcome(reboot ? Severity.Warning : Severity.Success,
                count == 1 ? "Réglage d'origine restauré" : $"{count} réglages d'origine restaurés",
                reboot ? "Redémarrez Windows pour que tout soit pris en compte." : "");
        }
        await RunAsync();
    }

    // ---- Contrôles par statut ----

    public ObservableCollection<DiagnosticItemViewModel> AttentionItems { get; } = [];

    public ObservableCollection<DiagnosticItemViewModel> UnverifiedItems { get; } = [];

    public ObservableCollection<DiagnosticItemViewModel> InfoItems { get; } = [];

    public ObservableCollection<DiagnosticItemViewModel> OkItems { get; } = [];

    /// <summary>Une analyse a-t-elle déjà été faite (la fenêtre la lance à sa première ouverture) ?</summary>
    public bool HasResults => AttentionItems.Count + UnverifiedItems.Count + InfoItems.Count + OkItems.Count > 0;

    [ObservableProperty] private string _attentionHeader = "";
    [ObservableProperty] private string _unverifiedHeader = "";
    [ObservableProperty] private string _infoHeader = "";
    [ObservableProperty] private string _okHeader = "";
    [ObservableProperty] private bool _hasAttention;
    [ObservableProperty] private bool _hasUnverified;
    [ObservableProperty] private bool _hasInfo;
    [ObservableProperty] private bool _hasOk;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(ApplyAllCommand), nameof(RestoreAllCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Le diagnostic lit la configuration sans rien modifier.";

    /// <summary>Contrôles « À corriger » ou « Non vérifié » : badge de la page dans la navigation.</summary>
    [ObservableProperty]
    private int _problemCount;

    private bool CanRun() => !IsBusy;

    /// <summary>Analyse (F5, « Actualiser », « Réessayer ») : lecture seule, aucun réglage n'est modifié.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        IsBusy = true;
        StatusText = "Analyse en cours…";
        try
        {
            // WMI et P/Invoke hors du thread UI.
            var results = await Task.Run(runner.RunAll);
            Present(results);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasResults));
        }
    }

    /// <summary>Affiche des résultats d'analyse (ceux de <see cref="RunAsync"/>, ou d'exemple pour les captures de l'interface).</summary>
    internal void Present(IReadOnlyList<DiagnosticResult> results)
    {
        AttentionItems.Clear();
        UnverifiedItems.Clear();
        InfoItems.Clear();
        OkItems.Clear();
        foreach (var result in results)
        {
            var fixVms = result.Fixes
                .Where(f => !fixes.IsActive(f.Change.Id))
                .Select(f => new FixViewModel(f, ApplyFixAsync))
                .ToList();
            // À corriger et Non vérifié : dépliés à la première analyse ; ensuite, l'état choisi par l'utilisateur.
            if (!_seenChecks.Contains(result.CheckId) && result.Status is DiagnosticStatus.NeedsAttention or DiagnosticStatus.Error)
            {
                _expanded.Add(result.CheckId);
            }
            _seenChecks.Add(result.CheckId);
            var item = new DiagnosticItemViewModel(result, fixVms, _expanded.Contains(result.CheckId), OnExpandedChanged);
            (result.Status switch
            {
                DiagnosticStatus.NeedsAttention => AttentionItems,
                DiagnosticStatus.Error => UnverifiedItems,
                DiagnosticStatus.Ok => OkItems,
                _ => InfoItems,
            }).Add(item);
        }
        AttentionHeader = $"À corriger · {AttentionItems.Count}";
        UnverifiedHeader = $"Non vérifié · {UnverifiedItems.Count}";
        InfoHeader = $"À savoir · {InfoItems.Count}";
        OkHeader = $"OK · {OkItems.Count}";
        HasAttention = AttentionItems.Count > 0;
        HasUnverified = UnverifiedItems.Count > 0;
        HasInfo = InfoItems.Count > 0;
        HasOk = OkItems.Count > 0;

        RefreshActiveOptimizations();
        RefreshOverview(results);
        ProblemCount = AttentionItems.Count + UnverifiedItems.Count;
        StatusText = $"Dernière analyse à {time.GetLocalNow():HH:mm}";
        OnPropertyChanged(nameof(HasResults));
    }

    /// <summary>Contrôles déjà vus pendant cette exécution (pour ne déplier d'office qu'à la première apparition).</summary>
    private readonly HashSet<string> _seenChecks = [];

    private void OnExpandedChanged(string checkId, bool expanded)
    {
        if (expanded) _expanded.Add(checkId);
        else _expanded.Remove(checkId);
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

        ShowOutcome(fix.Change.RequiresReboot ? Severity.Warning : Severity.Success, $"Optimisation appliquée : {fix.Change.Title}",
            fix.Change.RequiresReboot ? "Elle sera prise en compte au prochain redémarrage de Windows." : "Restaurable dans « Optimisations actives ».");
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
        else
        {
            var external = report.ModifiedExternally.Count > 0
                ? "Il avait été modifié entre-temps par un autre programme : c'est la valeur sauvegardée avant l'optimisation qui est revenue. "
                : "";
            ShowOutcome(change.RequiresReboot ? Severity.Warning : Severity.Success, $"Réglage d'origine restauré : {change.Title}",
                external + (change.RequiresReboot ? "Redémarrez Windows pour qu'il soit pris en compte." : ""));
        }

        await RunAsync();
    }

    private void ShowOutcome(Severity severity, string title, string message)
    {
        OutcomeSeverity = severity;
        OutcomeMessage = message;
        OutcomeTitle = title;
    }

    private void RefreshOverview(IReadOnlyList<DiagnosticResult> results)
    {
        var overview = _overview = OneClickOptimization.Overview(results, fixes.ActiveChanges);
        Verdict = overview.Verdict;
        var recommended = overview.Recommended.Count;
        Headline = overview.Verdict switch
        {
            DiagnosticVerdict.Recommended => recommended == 1 ? "1 optimisation recommandée" : $"{recommended} optimisations recommandées",
            DiagnosticVerdict.ManualActions => "Il reste des points à corriger de votre côté",
            DiagnosticVerdict.Incomplete => "Analyse incomplète",
            _ => "Votre PC est prêt pour le jeu",
        };
        VerdictDetail = overview.Verdict switch
        {
            DiagnosticVerdict.Recommended => recommended == 1
                ? "Sera appliquée après confirmation (restaurable) :"
                : "Seront appliquées après confirmation (chacune restaurable) :",
            DiagnosticVerdict.ManualActions => "OptiGame ne peut pas les corriger seul (BIOS, pilote, option avancée) : voir « À corriger ».",
            DiagnosticVerdict.Incomplete =>
                $"{FrenchText.Count(overview.Unverified.Count, "contrôle n'a", "contrôles n'ont")} pas pu s'exécuter : voir « Non vérifié ». Le reste est en ordre.",
            _ => "Rien à corriger. Les optimisations actives restent restaurables ci-dessous.",
        };
        CountsText = string.Join(" · ", new[]
        {
            FrenchText.Count(results.Count, "contrôle", "contrôles"),
            $"{overview.OkCount} OK",
            $"{overview.AttentionCount} à corriger",
            $"{overview.InfoCount} à savoir",
            overview.Unverified.Count > 0 ? $"{overview.Unverified.Count} non vérifié" + (overview.Unverified.Count > 1 ? "s" : "") : null,
        }.OfType<string>());
        RecommendedTitles = overview.Recommended.Select(c => c.Title).ToList();
        HasRecommended = recommended > 0;
        ApplyAllLabel = recommended == 1 ? "Appliquer l'optimisation…" : $"Appliquer les {recommended} optimisations…";
        HasOptional = overview.Optional.Count > 0;
        OptionalText = overview.Optional.Count == 1
            ? $"1 réglage facultatif dans « À savoir » : {overview.Optional[0].Title}."
            : $"{overview.Optional.Count} réglages facultatifs dans « À savoir » : {string.Join(", ", overview.Optional.Select(r => r.Title))}.";
        CanRestoreAllOptimizations = overview.Applied.Count > 0;
    }

    private void RefreshActiveOptimizations()
    {
        ActiveOptimizations.Clear();
        GameOptimizations.Clear();
        foreach (var change in fixes.ActiveChanges)
        {
            (GameChanges.IsGameChange(change.Id) ? GameOptimizations : ActiveOptimizations).Add(new AppliedFixViewModel(change, time, UndoAsync));
        }
        HasActiveOptimizations = ActiveOptimizations.Count > 0;
        HasGameOptimizations = GameOptimizations.Count > 0;
        ActiveHeader = $"Optimisations actives · {ActiveOptimizations.Count + GameOptimizations.Count}";
        GameHeader = $"Réglages propres à un jeu · {GameOptimizations.Count}";
    }
}

/// <summary>Un contrôle du Diagnostic : ligne repliable (statut par icône de forme et mot, résumé, explication d'abord).</summary>
public sealed partial class DiagnosticItemViewModel : ObservableObject
{
    private readonly Action<string, bool> _onExpandedChanged;

    public DiagnosticItemViewModel(DiagnosticResult result, IReadOnlyList<FixViewModel> fixes, bool isExpanded, Action<string, bool> onExpandedChanged)
    {
        Result = result;
        _onExpandedChanged = onExpandedChanged;
        _isExpanded = isExpanded;
        Fixes = fixes.Where(f => !f.IsAdvanced).ToList();
        AdvancedFixes = fixes.Where(f => f.IsAdvanced).ToList();
    }

    public DiagnosticResult Result { get; }

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value) => _onExpandedChanged(Result.CheckId, value);

    public string Title => FrenchText.Typeset(Result.IsEstimate ? $"{Result.Title} (estimation)" : Result.Title);

    public DiagnosticStatus Status => Result.Status;

    public string StatusLabel => Result.Status switch
    {
        DiagnosticStatus.Ok => "OK",
        DiagnosticStatus.NeedsAttention => "À corriger",
        DiagnosticStatus.Info => "À savoir",
        _ => "Non vérifié",
    };

    /// <summary>Glyphe de forme du statut (coche, triangle, i, croix) : le statut ne repose jamais sur la couleur seule.</summary>
    public string StatusGlyph => Result.Status switch
    {
        DiagnosticStatus.Ok => "",
        DiagnosticStatus.NeedsAttention => "",
        DiagnosticStatus.Info => "",
        _ => "",
    };

    public bool IsUnverified => Result.Status == DiagnosticStatus.Error;

    /// <summary>« Titre — statut : résumé » (lecteurs d'écran).</summary>
    public string AccessibleName => $"{Title} — {StatusLabel} : {Summary}";

    public string Summary => IsUnverified ? "Ce contrôle n'a pas pu s'exécuter." : FrenchText.Typeset(Result.Summary);

    /// <summary>Explication (« Pourquoi ») ; pour un contrôle non vérifié, le message technique passe dans les détails.</summary>
    public string Explanation => IsUnverified ? "" : FrenchText.Typeset(Result.Explanation);

    public bool HasExplanation => !string.IsNullOrWhiteSpace(Explanation);

    /// <summary>Valeurs mesurées (repliées) ; pour un contrôle non vérifié, l'erreur technique.</summary>
    public IReadOnlyList<string> Details => IsUnverified && !string.IsNullOrWhiteSpace(Result.Explanation) ? [Result.Explanation, .. Result.Details] : Result.Details;

    public bool HasDetails => Details.Count > 0;

    public string DetailsHeader => IsUnverified ? "Détails techniques" : "Valeurs mesurées";

    public IReadOnlyList<FixViewModel> Fixes { get; }

    public IReadOnlyList<FixViewModel> AdvancedFixes { get; }

    public bool HasAdvancedFixes => AdvancedFixes.Count > 0;

    public bool HasLink => Result.Link is not null;

    public string LinkLabel => Result.Link?.Label ?? "";

    public DiagnosticLinkTarget LinkTarget => Result.Link?.Target ?? default;
}

public sealed partial class FixViewModel(DiagnosticFix fix, Func<DiagnosticFix, Task> apply)
{
    public string Title => FrenchText.Typeset(fix.Change.Title);

    /// <summary>Libellé du bouton : l'action, suivie de « … » (une confirmation suit).</summary>
    public string ApplyLabel => $"{fix.Change.Title}…";

    public bool IsAdvanced => fix.IsAdvanced;

    public bool RequiresAdmin => fix.Change.RequiresAdmin;

    public bool RequiresReboot => fix.Change.RequiresReboot;

    [RelayCommand]
    private Task ApplyAsync() => apply(fix);
}

public sealed partial class AppliedFixViewModel(ChangeRecord change, TimeProvider time, Func<ChangeRecord, Task> undo)
{
    public string Title => FrenchText.Typeset(change.Title);

    public string What => change.What;

    public bool RequiresReboot => change.RequiresReboot;

    public string AppliedAt =>
        $"Appliquée {FrenchText.When(TimeZoneInfo.ConvertTime(change.AppliedAt, time.LocalTimeZone).DateTime, time.GetLocalNow().DateTime)}";

    [RelayCommand]
    private Task UndoAsync() => undo(change);
}
