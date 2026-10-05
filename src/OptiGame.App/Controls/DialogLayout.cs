using System.Windows;
using System.Windows.Controls;

namespace OptiGame.App.Controls;

/// <summary>Icône de l'en-tête d'un dialogue (forme + couleur de la gravité) ; None = pas d'icône.</summary>
public enum DialogIcon
{
    None,
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Gabarit commun des dialogues (style dans Themes/Theme.xaml) : en-tête (icône, instruction principale en titre de niveau 1,
/// description), corps défilant (le contenu), pied de boutons fixe (<see cref="Footer"/>). Les zones sont dans l'ordre visuel,
/// donc dans l'ordre de tabulation : corps, puis boutons.
/// </summary>
public class DialogLayout : ContentControl
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(DialogLayout));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(DialogLayout));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(DialogIcon), typeof(DialogLayout), new PropertyMetadata(DialogIcon.None));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(object), typeof(DialogLayout));

    public static readonly DependencyProperty IsBodyScrollableProperty = DependencyProperty.Register(
        nameof(IsBodyScrollable), typeof(bool), typeof(DialogLayout), new PropertyMetadata(true));

    /// <summary>Faux quand le corps défile lui-même (liste d'un sélecteur) : il reçoit alors la hauteur disponible.</summary>
    public bool IsBodyScrollable
    {
        get => (bool)GetValue(IsBodyScrollableProperty);
        set => SetValue(IsBodyScrollableProperty, value);
    }

    /// <summary>Instruction principale : ce que l'utilisateur décide (« Retirer Portal 2 de Mes jeux ? »).</summary>
    public string? Heading
    {
        get => (string?)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    /// <summary>Phrase d'accompagnement sous le titre (facultative).</summary>
    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public DialogIcon Icon
    {
        get => (DialogIcon)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Boutons, alignés à droite : action (verbe), puis « Annuler ».</summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }
}
