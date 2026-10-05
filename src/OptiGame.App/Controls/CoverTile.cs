using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OptiGame.App.Controls;

/// <summary>
/// Jaquette de la grille « Mes jeux », unique pour les jeux installés et non installés (style dans Themes/Theme.xaml) :
/// bouton focalisable (Entrée = action principale, touche Menu = toutes les actions), image décodée par ImageLoader (la vue lie
/// <see cref="Image"/> avec IsAsync), remplaçant à initiales, voile sur l'IMAGE seulement (les pastilles restent lisibles),
/// zones de pastilles (<see cref="TopLeft"/>, <see cref="TopRight"/>) et actions de survol (<see cref="Actions"/>, raccourcis
/// souris : au clavier, tout passe par le menu contextuel). Largeur = ImageLoader.GridCoverWidth (198).
/// </summary>
public class CoverTile : Button
{
    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(
        nameof(Image), typeof(ImageSource), typeof(CoverTile));

    public static readonly DependencyProperty InitialsProperty = DependencyProperty.Register(
        nameof(Initials), typeof(string), typeof(CoverTile));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(CoverTile));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(CoverTile));

    public static readonly DependencyProperty CaptionGlyphProperty = DependencyProperty.Register(
        nameof(CaptionGlyph), typeof(string), typeof(CoverTile));

    public static readonly DependencyProperty IsDimmedProperty = DependencyProperty.Register(
        nameof(IsDimmed), typeof(bool), typeof(CoverTile), new PropertyMetadata(false));

    public static readonly DependencyProperty TopLeftProperty = DependencyProperty.Register(
        nameof(TopLeft), typeof(object), typeof(CoverTile));

    public static readonly DependencyProperty TopRightProperty = DependencyProperty.Register(
        nameof(TopRight), typeof(object), typeof(CoverTile));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(CoverTile));

    public ImageSource? Image
    {
        get => (ImageSource?)GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    /// <summary>Initiales du jeu, affichées tant qu'il n'y a pas de jaquette.</summary>
    public string? Initials
    {
        get => (string?)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Ligne sous le titre (temps de jeu, genres).</summary>
    public string? Caption
    {
        get => (string?)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public string? CaptionGlyph
    {
        get => (string?)GetValue(CaptionGlyphProperty);
        set => SetValue(CaptionGlyphProperty, value);
    }

    /// <summary>Jeu non installé, désinstallé ou sur un disque absent : image atténuée, titre secondaire.</summary>
    public bool IsDimmed
    {
        get => (bool)GetValue(IsDimmedProperty);
        set => SetValue(IsDimmedProperty, value);
    }

    /// <summary>Coin haut gauche : épingle, pastilles d'état.</summary>
    public object? TopLeft
    {
        get => GetValue(TopLeftProperty);
        set => SetValue(TopLeftProperty, value);
    }

    /// <summary>Coin haut droit : note.</summary>
    public object? TopRight
    {
        get => GetValue(TopRightProperty);
        set => SetValue(TopRightProperty, value);
    }

    /// <summary>Boutons affichés au survol de la souris (doublés par le menu contextuel pour le clavier).</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <summary>Zoom au survol seulement si les animations sont activées (Paramètres › Général, selon Windows par défaut).</summary>
    public static bool AnimationsEnabled => Services.UiMotion.Enabled;
}
