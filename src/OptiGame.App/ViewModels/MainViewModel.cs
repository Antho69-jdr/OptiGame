using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OptiGame.App.Services;

namespace OptiGame.App.ViewModels;

/// <summary>Fenêtre principale : navigation latérale et page affichée.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(
        DiagnosticViewModel diagnostic,
        DriversViewModel drivers,
        LibraryViewModel library,
        MeasuresViewModel measures,
        SessionViewModel session,
        SettingsViewModel settings,
        NavigationService navigation)
    {
        Diagnostic = diagnostic;
        Library = library;
        Measures = measures;
        Session = session;
        Settings = settings;

        // Glyphes Segoe Fluent Icons : manette, diagnostic, téléchargement (pilotes), courbe, engrenage.
        NavItems =
        [
            new NavItem("Mes jeux", "", library, this),
            new NavItem("Diagnostic", "", diagnostic, this),
            new NavItem("Pilotes", "", drivers, this),
            new NavItem("Mesures", "", measures, this),
            new NavItem("Paramètres", "", settings, this),
        ];
        NavItems[0].IsSelected = true;
        navigation.NavigateRequested += Navigate;
    }

    public string Title => "OptiGame";

    public DiagnosticViewModel Diagnostic { get; }

    public LibraryViewModel Library { get; }

    public MeasuresViewModel Measures { get; }

    public SessionViewModel Session { get; }

    public SettingsViewModel Settings { get; }

    public ObservableCollection<NavItem> NavItems { get; }

    /// <summary>ViewModel de la page affichée ; la vue est choisie par DataTemplate (MainWindow.xaml).</summary>
    [ObservableProperty]
    private object? _currentPage;

    public void Navigate(object page)
    {
        foreach (var item in NavItems)
        {
            item.SetSelectedSilently(ReferenceEquals(item.Page, page));
        }
        CurrentPage = page;
    }
}

public sealed partial class NavItem(string title, string glyph, object page, MainViewModel main) : ObservableObject
{
    public string Title { get; } = title;

    public string Glyph { get; } = glyph;

    public object Page { get; } = page;

    [ObservableProperty]
    private bool _isSelected;

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
