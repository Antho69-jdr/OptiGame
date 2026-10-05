using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace OptiGame.App.Controls;

/// <summary>
/// Bouton « glyphe + libellé » dont le libellé est toujours le nom accessible, même quand il n'est pas affiché (icône seule :
/// il devient aussi l'infobulle). Remplace les boutons dont le contenu était un StackPanel, que les lecteurs d'écran
/// annonçaient sans nom. S'utilise avec n'importe quel style Button.* (le contenu est construit ici, pas dans le gabarit).
/// </summary>
public class IconButton : Button
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(IconButton), new PropertyMetadata(null, OnContentPartChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(IconButton), new PropertyMetadata(null, OnContentPartChanged));

    public static readonly DependencyProperty IsLabelVisibleProperty = DependencyProperty.Register(
        nameof(IsLabelVisible), typeof(bool), typeof(IconButton), new PropertyMetadata(true, OnContentPartChanged));

    private readonly TextBlock _glyph = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private string? _labelToolTip;

    public IconButton()
    {
        _glyph.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
        _glyph.SetResourceReference(TextBlock.FontSizeProperty, "IconSize.Body");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(_glyph);
        panel.Children.Add(_label);
        Content = panel;
        Refresh();
    }

    /// <summary>Glyphe Segoe Fluent Icons (ex. « &#xE72C; »).</summary>
    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Libellé : texte du bouton, nom accessible et, en icône seule, infobulle.</summary>
    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Faux = icône seule (barres d'outils compactes, fermeture d'un bandeau).</summary>
    public bool IsLabelVisible
    {
        get => (bool)GetValue(IsLabelVisibleProperty);
        set => SetValue(IsLabelVisibleProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new IconButtonAutomationPeer(this);

    private static void OnContentPartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((IconButton)d).Refresh();

    private void Refresh()
    {
        var hasGlyph = !string.IsNullOrEmpty(Glyph);
        var showLabel = IsLabelVisible && !string.IsNullOrEmpty(Label);
        _glyph.Text = Glyph;
        _glyph.Visibility = hasGlyph ? Visibility.Visible : Visibility.Collapsed;
        _label.Text = Label;
        _label.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
        _label.Margin = hasGlyph ? new Thickness(8, 0, 0, 0) : new Thickness(0);

        // Infobulle = libellé en icône seule, sauf si la vue en a posé une autre (comparaison d'instance : une infobulle
        // explicite au même texte reste celle de la vue).
        var ownsToolTip = _labelToolTip is not null && ReferenceEquals(ToolTip, _labelToolTip);
        if (!showLabel && !string.IsNullOrEmpty(Label) && (ToolTip is null || ownsToolTip))
        {
            ToolTip = _labelToolTip = Label;
        }
        else if (ownsToolTip && (showLabel || string.IsNullOrEmpty(Label)))
        {
            ClearValue(ToolTipProperty);
            _labelToolTip = null;
        }
    }

    private sealed class IconButtonAutomationPeer(IconButton owner) : ButtonAutomationPeer(owner)
    {
        protected override string GetNameCore() =>
            AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : owner.Label ?? "";
    }
}
