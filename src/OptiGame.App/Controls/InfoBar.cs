using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace OptiGame.App.Controls;

/// <summary>Gravité d'un message (InfoBar, badge) : décide du fond, de l'icône de forme et du mot lu par le lecteur d'écran.</summary>
public enum Severity
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Bandeau de message dans la fenêtre, sur le modèle de l'InfoBar de WinUI : gravité (fond + icône de forme), titre, message,
/// actions (le contenu : un ou deux boutons) et fermeture facultative. Style dans Themes/Theme.xaml. Annoncé aux lecteurs
/// d'écran quand il apparaît ou change (région active, « assertive » pour une erreur). Les erreurs importantes passent par
/// ici en plus des notifications Windows, qui peuvent être masquées.
/// </summary>
public class InfoBar : ContentControl
{
    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(Severity), typeof(InfoBar), new PropertyMetadata(Severity.Info, OnTextChanged));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(InfoBar), new PropertyMetadata(null, OnTextChanged));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(InfoBar), new PropertyMetadata(null, OnTextChanged));

    public static readonly DependencyProperty IsClosableProperty = DependencyProperty.Register(
        nameof(IsClosable), typeof(bool), typeof(InfoBar), new PropertyMetadata(false));

    public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(
        nameof(CloseCommand), typeof(ICommand), typeof(InfoBar));

    public static readonly DependencyProperty CloseLabelProperty = DependencyProperty.Register(
        nameof(CloseLabel), typeof(string), typeof(InfoBar), new PropertyMetadata("Fermer"));

    public InfoBar()
    {
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true) Announce();
        };
    }

    public Severity Severity
    {
        get => (Severity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsClosable
    {
        get => (bool)GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    /// <summary>Nom accessible du bouton de fermeture (« Fermer » par défaut ; préciser : « Masquer cette alerte »).</summary>
    public string CloseLabel
    {
        get => (string)GetValue(CloseLabelProperty);
        set => SetValue(CloseLabelProperty, value);
    }

    /// <summary>Mot de la gravité, lu avant le message (la couleur seule ne porte jamais le sens).</summary>
    public static string SeverityWord(Severity severity) => severity switch
    {
        Severity.Success => "Réussite",
        Severity.Warning => "Attention",
        Severity.Error => "Erreur",
        _ => "Information",
    };

    internal string AccessibleText =>
        string.Join(" ", new[] { SeverityWord(Severity) + " :", Title, Message }.Where(s => !string.IsNullOrWhiteSpace(s)));

    protected override AutomationPeer OnCreateAutomationPeer() => new InfoBarAutomationPeer(this);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((InfoBar)d).Announce();

    private void Announce()
    {
        if (!IsVisible) return;
        (UIElementAutomationPeer.FromElement(this) ?? UIElementAutomationPeer.CreatePeerForElement(this))
            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private sealed class InfoBarAutomationPeer(InfoBar owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() =>
            AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : owner.AccessibleText;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.StatusBar;

        protected override string GetClassNameCore() => nameof(InfoBar);
    }
}
