using System.Runtime.InteropServices;

namespace OptiGame.Platform.Display;

/// <summary>Tailles d'icône de Windows, en pixels, à l'échelle d'affichage du système (100 % = 16, 125 % = 20, 150 % = 24).</summary>
public static class IconMetrics
{
    private const int SmCxSmIcon = 49;

    /// <summary>Petites icônes (zone de notification, barre de titre) ; 16 si Windows ne répond pas.</summary>
    public static int SmallIconPixels()
    {
        try
        {
            var size = GetSystemMetricsForDpi(SmCxSmIcon, GetDpiForSystem());
            return size > 0 ? size : 16;
        }
        catch (EntryPointNotFoundException)
        {
            return 16; // Windows antérieur à 10 version 1607
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
