using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace OptiGame.App.Controls;

/// <summary>
/// Exigences d'une optimisation, en badges « Droits administrateur » (bouclier) et « Redémarrage requis » : un seul libellé
/// pour toute l'appli (confirmations, Diagnostic, fiche du jeu). Style dans Themes/Theme.xaml.
/// </summary>
public class RequirementBadges : Control
{
    public static readonly DependencyProperty RequiresAdminProperty = DependencyProperty.Register(
        nameof(RequiresAdmin), typeof(bool), typeof(RequirementBadges), new PropertyMetadata(false));

    public static readonly DependencyProperty RequiresRebootProperty = DependencyProperty.Register(
        nameof(RequiresReboot), typeof(bool), typeof(RequirementBadges), new PropertyMetadata(false));

    public bool RequiresAdmin
    {
        get => (bool)GetValue(RequiresAdminProperty);
        set => SetValue(RequiresAdminProperty, value);
    }

    public bool RequiresReboot
    {
        get => (bool)GetValue(RequiresRebootProperty);
        set => SetValue(RequiresRebootProperty, value);
    }

    /// <summary>Texte des exigences (« Droits administrateur · Redémarrage requis ») ; vide s'il n'y en a pas.</summary>
    public static string Describe(bool requiresAdmin, bool requiresReboot) =>
        string.Join(" · ", new[] { requiresAdmin ? "Droits administrateur" : null, requiresReboot ? "Redémarrage requis" : null }.OfType<string>());

    protected override AutomationPeer OnCreateAutomationPeer() => new RequirementBadgesAutomationPeer(this);

    private sealed class RequirementBadgesAutomationPeer(RequirementBadges owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() =>
            AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : Describe(owner.RequiresAdmin, owner.RequiresReboot);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override bool IsContentElementCore() => !string.IsNullOrEmpty(GetNameCore());
    }
}
