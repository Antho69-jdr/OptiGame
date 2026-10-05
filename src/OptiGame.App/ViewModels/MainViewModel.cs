using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Text;

namespace OptiGame.App.ViewModels;

/// <summary>
/// Coque de la fenêtre principale : navigation latérale (pages en haut, Paramètres en pied), badges d'état, alertes
/// persistantes, raccourcis clavier et page affichée. Singleton : la fenêtre est recréée après chaque partie, l'état reste ici.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    // Glyphes Segoe Fluent Icons : manette, diagnostic, composant (pilotes ; le téléchargement E896 reste à la mise à jour),
    // courbe, engrenage.
    private const string GamesGlyph = "";
    private const string DiagnosticGlyph = "";
    private const string DriversGlyph = "";
    private const string MeasuresGlyph = "";
    private const string SettingsGlyph = "";

    private readonly DriversViewModel _drivers;

    public MainViewModel(
        DiagnosticViewModel diagnostic,
        DriversViewModel drivers,
        LibraryViewModel library,
        MeasuresViewModel measures,
        SessionViewModel session,
        SettingsViewModel settings,
        UpdateService updates,
        ShellAlerts alerts,
        NavigationService navigation)
    {
        Updates = updates;
        Alerts = alerts;
        Diagnostic = diagnostic;
        Library = library;
        Measures = measures;
        Session = session;
        Settings = settings;
        _drivers = drivers;

        NavItems =
        [
            new NavItem("Mes jeux", GamesGlyph, library, this),
            new NavItem("Diagnostic", DiagnosticGlyph, diagnostic, this),
            new NavItem("Pilotes", DriversGlyph, drivers, this),
            new NavItem("Mesures", MeasuresGlyph, measures, this),
        ];
        SettingsItem = new NavItem("Paramètres", SettingsGlyph, settings, this);
        NavItems[0].IsSelected = true;
        navigation.NavigateRequested += Navigate;

        // Badges : contrôles à corriger, pilotes plus récents, mise à jour d'OptiGame.
        diagnostic.PropertyChanged += OnBadgeSourceChanged;
        drivers.PropertyChanged += OnBadgeSourceChanged;
        updates.PropertyChanged += OnBadgeSourceChanged;
        RefreshBadges();
    }

    public string Title => "OptiGame";

    public DiagnosticViewModel Diagnostic { get; }

    public LibraryViewModel Library { get; }

    public MeasuresViewModel Measures { get; }

    public SessionViewModel Session { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>Bandeaux « mise à jour disponible » et « OptiGame a été mis à jour ».</summary>
    public UpdateService Updates { get; }

    /// <summary>Alertes persistantes (erreurs de détection, de restauration…), en haut de chaque page.</summary>
    public ShellAlerts Alerts { get; }

    /// <summary>Pages de travail, en haut de la barre latérale.</summary>
    public ObservableCollection<NavItem> NavItems { get; }

    /// <summary>Paramètres, en pied de la barre latérale (usage Windows 11).</summary>
    public NavItem SettingsItem { get; }

    private IEnumerable<NavItem> AllItems => NavItems.Append(SettingsItem);

    /// <summary>ViewModel de la page affichée ; la vue est choisie par DataTemplate (MainWindow.xaml).</summary>
    [ObservableProperty]
    private object? _currentPage;

    /// <summary>Texte lu par les lecteurs d'écran à chaque changement de page (région active de la fenêtre).</summary>
    [ObservableProperty]
    private string _announcement = "";

    public void Navigate(object page)
    {
        foreach (var item in AllItems)
        {
            item.SetSelectedSilently(ReferenceEquals(item.Page, page));
        }
        if (ReferenceEquals(CurrentPage, page)) return;
        CurrentPage = page;
        if (AllItems.FirstOrDefault(i => ReferenceEquals(i.Page, page)) is { } selected)
        {
            Announcement = $"Page {selected.Title}";
        }
    }

    /// <summary>Clic sur une entrée : sur la page déjà affichée, « Mes jeux » revient à la grille (fiche fermée, avec sa garde).</summary>
    [RelayCommand]
    private void Invoke(NavItem? item)
    {
        if (item is null) return;
        if (ReferenceEquals(CurrentPage, item.Page) && ReferenceEquals(item.Page, Library)) Library.ReturnToGrid();
    }

    /// <summary>Ctrl+1 … Ctrl+5 : pages dans l'ordre de la barre latérale (Paramètres en dernier).</summary>
    [RelayCommand]
    private void NavigateTo(string? position)
    {
        if (!int.TryParse(position, out var index)) return;
        var items = AllItems.ToList();
        if (index >= 1 && index <= items.Count) Navigate(items[index - 1].Page);
    }

    /// <summary>Échap, Alt+← ou bouton « précédent » de la souris : fiche du jeu → grille.</summary>
    [RelayCommand]
    private void Back()
    {
        if (ReferenceEquals(CurrentPage, Library)) Library.ReturnToGrid();
    }

    /// <summary>F5 : actualise la page affichée (bibliothèques, analyse, pilotes) ; rien ne s'écrit.</summary>
    [RelayCommand]
    private void Refresh()
    {
        System.Windows.Input.ICommand? command = CurrentPage switch
        {
            LibraryViewModel library when library.OpenGame is null => library.RefreshLibraryCommand,
            DiagnosticViewModel diagnostic => diagnostic.RunCommand,
            DriversViewModel drivers => drivers.SearchCommand,
            _ => null,
        };
        if (command?.CanExecute(null) == true) command.Execute(null);
    }

    /// <summary>Ctrl+F : recherche de Mes jeux.</summary>
    [RelayCommand]
    private void FocusSearch() => Library.RequestSearchFocus();

    private void OnBadgeSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiagnosticViewModel.ProblemCount) or nameof(DriversViewModel.AvailableCount)
            or nameof(UpdateService.HasPackage) or nameof(UpdateService.Phase))
        {
            RefreshBadges();
        }
    }

    private void RefreshBadges()
    {
        var problems = Diagnostic.ProblemCount;
        NavItems[1].SetBadge(problems > 0 ? problems.ToString() : null, FrenchText.Count(problems, "point à corriger", "points à corriger"));
        var drivers = _drivers.AvailableCount;
        NavItems[2].SetBadge(drivers > 0 ? drivers.ToString() : null,
            FrenchText.Count(drivers, "pilote plus récent disponible", "pilotes plus récents disponibles"));
        SettingsItem.SetBadge(Updates.HasPackage ? "1" : null, "mise à jour d'OptiGame disponible");
    }
}

public sealed partial class NavItem(string title, string glyph, object page, MainViewModel main) : ObservableObject
{
    public string Title { get; } = title;

    public string Glyph { get; } = glyph;

    public object Page { get; } = page;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Pastille (nombre) affichée sur l'entrée ; null = aucune.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge), nameof(AccessibleName))]
    private string? _badge;

    private string _badgeDescription = "";

    public bool HasBadge => Badge is not null;

    /// <summary>Nom lu par les lecteurs d'écran : « Diagnostic, 3 points à corriger » (la pastille n'est pas que visuelle).</summary>
    public string AccessibleName => HasBadge ? $"{Title}, {_badgeDescription}" : Title;

    /// <summary>Info-bulle en barre compacte : le titre, et l'état s'il y en a un.</summary>
    public string ToolTip => AccessibleName;

    public void SetBadge(string? badge, string description)
    {
        _badgeDescription = description;
        Badge = badge;
        OnPropertyChanged(nameof(ToolTip));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) main.Navigate(Page);
    }

    internal void SetSelectedSilently(bool value)
    {
#pragma warning disable MVVMTK0034 // Mise à jour sans rappel : évite une navigation récursive.
        if (_isSelected == value) return;
        _isSelected = value;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(IsSelected));
    }
}
