using System.Windows;
using System.Windows.Input;

namespace OptiGame.App.Dialogs;

/// <summary>
/// Fenêtre de base de tous les dialogues d'OptiGame (thème sombre, hors barre des tâches, centrée sur la fenêtre d'origine,
/// hauteur ajustée au contenu mais jamais plus grande que la zone de travail : le corps du <see cref="Controls.DialogLayout"/>
/// défile). Le focus initial va à <see cref="InitialFocus"/> (« Annuler » dans les confirmations). Toujours ouverte par
/// DialogService (modale, possédée par la fenêtre active).
/// </summary>
public class DialogWindow : Window
{
    /// <summary>Marge gardée autour d'un dialogue qui remplirait l'écran (125-150 % d'échelle).</summary>
    private const double ScreenMargin = 48;

    public DialogWindow()
    {
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        Width = 520;

        SourceInitialized += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            MaxHeight = Math.Max(MinHeight, area.Height - ScreenMargin);
            MaxWidth = Math.Max(MinWidth, area.Width - ScreenMargin);
        };
        Loaded += (_, _) =>
        {
            if (InitialFocus is { } target)
            {
                target.Focus();
                Keyboard.Focus(target);
            }
        };
    }

    /// <summary>Élément qui reçoit le focus à l'ouverture.</summary>
    public IInputElement? InitialFocus { get; set; }
}
