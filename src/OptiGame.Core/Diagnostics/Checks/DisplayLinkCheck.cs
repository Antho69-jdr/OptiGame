using OptiGame.Core.Abstractions;
using OptiGame.Core.Display;

namespace OptiGame.Core.Diagnostics.Checks;

/// <summary>
/// Branchement des écrans (rien à corriger par logiciel : le conseil est matériel) :
/// <list type="bullet">
/// <item>Écran capable de plus que ce que Windows propose à sa résolution native (EDID contre modes de Windows) : câble, port
/// ou adaptateur qui limite le débit (ex. HDMI 1.4 ou câble ancien, 144 Hz bloqué à 60 Hz).</item>
/// <item>PC de bureau : écran PRINCIPAL (celui des jeux) branché sur la sortie de la carte mère (carte graphique intégrée) alors qu'une carte graphique
/// dédiée est présente : les jeux tournent alors sur la carte intégrée, ou leurs images font un détour. Jamais signalé sur un
/// portable (écran interne et sorties souvent câblés sur la carte intégrée, c'est normal).</item>
/// </list>
/// </summary>
public sealed class DisplayLinkCheck(IDisplayLinkInfo links, IGpuInfoProvider gpus, IPowerStatusProvider power) : IDiagnosticCheck
{
    /// <summary>Écart toléré entre l'EDID et Windows (arrondis : 143,85 Hz annoncés, 144 Hz proposés).</summary>
    private const int ToleranceHz = 5;

    public string Id => "display.link";

    public string Title => "Branchement des écrans";

    public DiagnosticResult Run()
    {
        var all = links.GetLinks();
        if (all.Count == 0) return Result(DiagnosticStatus.Info, "Aucun écran actif détecté.", "", []);

        var problems = new List<string>();
        var advice = new List<string>();
        var details = new List<string>();
        var desktop = !power.GetStatus().HasBattery;
        var physical = gpus.GetAdapters().Where(g => g.IsPhysical).Select(g => g.Name).ToList();
        var dedicated = physical.FirstOrDefault(IsDedicated);

        foreach (var link in all)
        {
            var label = string.IsNullOrWhiteSpace(link.MonitorName) ? link.DeviceName : link.MonitorName;
            var timings = link.Edid is { } edid ? Edid.Timings(edid) : [];
            var native = Edid.Native(timings);
            var edidMax = native is { } n ? Edid.MaxRefreshAt(timings, n.Width, n.Height) : null;
            details.Add($"{label} : {ConnectionText(link.Connection)}, carte « {link.GpuName} »" +
                        (native is { } res && edidMax is { } max
                            ? $", annonce {res.Width}×{res.Height} à {max} Hz, Windows propose {link.WindowsMaxHzAtNative?.ToString() ?? "?"} Hz à cette résolution."
                            : ", EDID illisible."));

            if (edidMax is { } capable && link.WindowsMaxHzAtNative is { } offered && capable - offered > ToleranceHz)
            {
                problems.Add($"{label} : capable de {capable} Hz, limité à {offered} Hz");
                advice.Add($"{label} est limité à {offered} Hz par sa connexion ({ConnectionText(link.Connection)}) : " + LinkAdvice(link.Connection));
            }

            if (desktop && link.IsPrimary && dedicated is not null && !IsDedicated(link.GpuName) && IsExternal(link.Connection))
            {
                problems.Add($"{label} : branché sur la carte mère");
                advice.Add($"{label} est branché sur une sortie de la carte mère (carte « {link.GpuName} ») : rebranchez-le sur une sortie de " +
                           $"la carte graphique « {dedicated} », sinon les jeux ne profitent pas pleinement de celle-ci.");
            }
        }

        return problems.Count == 0
            ? Result(DiagnosticStatus.Ok, "Écrans branchés sans limite de fréquence.", "", details)
            : Result(DiagnosticStatus.NeedsAttention, string.Join(" ; ", problems) + ".", string.Join("\n\n", advice), details);
    }

    private static string LinkAdvice(DisplayConnection connection) => connection switch
    {
        DisplayConnection.Hdmi => "utilisez un câble HDMI « High Speed » ou « Ultra High Speed » branché sur un port HDMI 2.0 ou 2.1 des deux côtés, " +
                                  "ou passez en DisplayPort. Les sorties HDMI 1.4 s'arrêtent souvent à 60 Hz en haute résolution.",
        DisplayConnection.DisplayPort => "essayez un autre câble DisplayPort (certifié, sans adaptateur ni rallonge) et vérifiez dans le menu de l'écran " +
                                         "que la version DisplayPort la plus récente est choisie.",
        DisplayConnection.Dvi or DisplayConnection.Vga => "ces connexions limitent la fréquence : utilisez DisplayPort ou HDMI.",
        _ => "essayez un câble DisplayPort ou HDMI récent, branché directement sur la carte graphique (sans adaptateur ni station d'accueil).",
    };

    private static string ConnectionText(DisplayConnection connection) => connection switch
    {
        DisplayConnection.Hdmi => "HDMI",
        DisplayConnection.DisplayPort => "DisplayPort",
        DisplayConnection.UsbC => "USB-C",
        DisplayConnection.Dvi => "DVI",
        DisplayConnection.Vga => "VGA",
        DisplayConnection.Internal => "écran intégré",
        DisplayConnection.Wireless => "sans fil",
        _ => "connexion inconnue",
    };

    private static bool IsExternal(DisplayConnection connection) => connection is not (DisplayConnection.Internal or DisplayConnection.Wireless or DisplayConnection.Other);

    /// <summary>Carte graphique dédiée : NVIDIA, ou Radeon RX / Pro d'AMD, ou Intel Arc (les « Radeon Graphics » / UHD sont intégrées).</summary>
    public static bool IsDedicated(string gpuName) =>
        gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
        || gpuName.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase)
        || gpuName.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase)
        || gpuName.Contains("Arc(TM) A", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("Arc A", StringComparison.OrdinalIgnoreCase)
        || gpuName.Contains("Arc(TM) B", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("Arc B", StringComparison.OrdinalIgnoreCase);

    private DiagnosticResult Result(DiagnosticStatus status, string summary, string explanation, IReadOnlyList<string> details) => new()
    {
        CheckId = Id,
        Title = Title,
        Status = status,
        Summary = summary,
        Explanation = explanation,
        Details = details,
    };
}
