using OptiGame.Core.Abstractions;
using OptiGame.Core.Changes;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Diagnostics.Checks;

public sealed class DisplayRefreshRateCheck(IDisplayInfoProvider displays) : IDiagnosticCheck
{
    public string Id => "display.refresh-rate";

    public string Title => "Taux de rafraîchissement des écrans";

    public DiagnosticResult Run()
    {
        var all = displays.GetDisplays();
        if (all.Count == 0)
        {
            return Result(DiagnosticStatus.Info, "Aucun écran actif détecté.", [], []);
        }

        var details = new List<string>();
        var fixes = new List<DiagnosticFix>();
        foreach (var d in all)
        {
            details.Add($"{Label(d)} : {d.Width}×{d.Height} à {d.CurrentHz} Hz (max. {d.MaxHzAtCurrentResolution} Hz à cette résolution)");

            if (d.CurrentHz < d.MaxHzAtCurrentResolution)
            {
                fixes.Add(new DiagnosticFix(new ReversibleChange
                {
                    Id = $"fix.display.refresh-rate.{d.DeviceName}",
                    Title = $"Passer {Label(d)} à {d.MaxHzAtCurrentResolution} Hz",
                    What = $"Mode d'affichage de {Label(d)} : {d.Width}×{d.Height} à {d.CurrentHz} Hz → {d.MaxHzAtCurrentResolution} Hz (même résolution).",
                    Why = "L'écran affiche moins d'images par seconde qu'il ne le peut : la fluidité est plafonnée quel que soit le nombre de FPS du jeu.",
                    Writes =
                    [
                        new SettingWrite(KnownSettings.DisplayMode(d.DeviceName),
                            SettingValue.String(DisplayModeText.Format(d.Width, d.Height, d.MaxHzAtCurrentResolution))),
                    ],
                }));
            }
        }

        if (fixes.Count == 0)
        {
            return Result(DiagnosticStatus.Ok, "Tous les écrans utilisent leur taux maximal.", details, fixes);
        }

        var worst = all.First(d => d.CurrentHz < d.MaxHzAtCurrentResolution);
        return Result(DiagnosticStatus.NeedsAttention,
            $"{Label(worst)} est à {worst.CurrentHz} Hz alors que {worst.MaxHzAtCurrentResolution} Hz est disponible.",
            details, fixes);
    }

    private DiagnosticResult Result(DiagnosticStatus status, string summary, IReadOnlyList<string> details, IReadOnlyList<DiagnosticFix> fixes) => new()
    {
        CheckId = Id,
        Title = Title,
        Status = status,
        Summary = summary,
        Details = details,
        Fixes = fixes,
        Explanation =
            "Windows n'active pas toujours le taux de rafraîchissement maximal (première installation, mise à jour du " +
            "pilote, changement de câble…). Si le taux attendu n'apparaît pas du tout, vérifiez le câble (DisplayPort ou " +
            "HDMI récent) et le port utilisé (celui de la carte graphique, pas de la carte mère).",
    };

    private static string Label(DisplayInfo d) =>
        string.IsNullOrWhiteSpace(d.MonitorName) ? d.DeviceName : $"{d.MonitorName} ({d.DeviceName})";
}

/// <summary>Format texte d'un mode d'affichage, ex. « 2560x1440@164 ».</summary>
public static class DisplayModeText
{
    public static string Format(int width, int height, int hz) => $"{width}x{height}@{hz}";

    public static bool TryParse(string? text, out int width, out int height, out int hz)
    {
        width = height = hz = 0;
        if (text is null) return false;
        var at = text.Split('@');
        if (at.Length != 2) return false;
        var wh = at[0].Split('x');
        return wh.Length == 2
            && int.TryParse(wh[0], out width)
            && int.TryParse(wh[1], out height)
            && int.TryParse(at[1], out hz);
    }
}
