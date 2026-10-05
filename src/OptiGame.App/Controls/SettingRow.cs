using System.Windows;
using System.Windows.Controls;

namespace OptiGame.App.Controls;

/// <summary>
/// Ligne de réglage façon Paramètres de Windows 11 (style dans Themes/Theme.xaml) : icône, titre, une phrase de description,
/// le contrôle à droite (le contenu). Les explications longues vont dans <see cref="Details"/>, repliées (« En savoir plus »).
/// Le contrôle porte son propre nom accessible (le titre de la ligne).
/// </summary>
public class SettingRow : ContentControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingRow));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingRow));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingRow));

    public static readonly DependencyProperty DetailsProperty = DependencyProperty.Register(
        nameof(Details), typeof(object), typeof(SettingRow));

    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Glyphe Segoe Fluent Icons (facultatif).</summary>
    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Contenu replié sous la description (« En savoir plus ») : explications, liste, champs secondaires.</summary>
    public object? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(SettingRow));

    public static readonly DependencyProperty IsSubSettingProperty = DependencyProperty.Register(
        nameof(IsSubSetting), typeof(bool), typeof(SettingRow), new PropertyMetadata(false));

    /// <summary>Arrondi de la ligne : carte seule (style par défaut) ou aucun dans une carte de groupe (style SettingsCard).</summary>
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Sous-réglage : aligné sur le texte du réglage parent (sous son titre, après son icône).</summary>
    public bool IsSubSetting
    {
        get => (bool)GetValue(IsSubSettingProperty);
        set => SetValue(IsSubSettingProperty, value);
    }
}
