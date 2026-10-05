using System.Windows;
using OptiGame.Core.Settings;

namespace OptiGame.App.Services;

/// <summary>
/// Animer ou non l'interface (dock, onde au clic, zoom des jaquettes) : réglage « Animations » de Paramètres, qui suit par
/// défaut « Effets d'animation » de Windows. Lu à chaque animation : un changement s'applique sans redémarrer.
/// </summary>
public static class UiMotion
{
    private static volatile int _mode = (int)UiAnimations.FollowWindows;

    public static void Attach(AppSettingsStore settings)
    {
        _mode = (int)settings.Get().UiAnimations;
        settings.Changed += (_, _) => _mode = (int)settings.Get().UiAnimations;
    }

    public static bool Enabled => Motion.IsEnabled((UiAnimations)_mode, SystemParameters.ClientAreaAnimation);
}
