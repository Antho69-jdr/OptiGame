using OptiGame.Core.Abstractions;

namespace OptiGame.Core.Diagnostics.Checks;

public sealed class RamSpeedCheck(IMemoryInfoProvider memory) : IDiagnosticCheck
{
    private const uint Ddr4 = 26;
    private const uint Ddr5 = 34;

    private const string XmpAdvice =
        "Activez le profil XMP (Intel), EXPO (AMD) ou DOCP (ASUS) dans le BIOS/UEFI de la carte mère : c'est le " +
        "profil de vitesse pour lequel le kit est vendu. Ce réglage ne peut pas être fait depuis Windows. En cas " +
        "d'instabilité après activation, mettez à jour le BIOS ou revenez au réglage automatique.";

    public string Id => "memory.speed";

    public string Title => "Vitesse de la RAM";

    public DiagnosticResult Run()
    {
        var modules = memory.GetMemoryInfo().Modules;
        if (modules.Count == 0)
        {
            return Result(DiagnosticStatus.Info, "Informations mémoire indisponibles.", "", []);
        }

        var details = modules.Select(m =>
            $"{Slot(m)} : {m.CapacityBytes / (1024 * 1024 * 1024)} Go, configurée à {Speed(m.ConfiguredSpeedMts)}, " +
            $"nominale {Speed(m.SpeedMts)}{(string.IsNullOrWhiteSpace(m.PartNumber) ? "" : $" — réf. {m.PartNumber.Trim()}")}").ToList();

        var slower = modules.Where(m => m.SpeedMts > 0 && m.ConfiguredSpeedMts > 0 && m.ConfiguredSpeedMts < m.SpeedMts).ToList();
        if (slower.Count > 0)
        {
            var m = slower[0];
            return Result(DiagnosticStatus.NeedsAttention,
                $"La RAM tourne à {Speed(m.ConfiguredSpeedMts)} au lieu de {Speed(m.SpeedMts)}.",
                "La RAM ne fonctionne pas à sa vitesse nominale, ce qui réduit les performances CPU dans les jeux " +
                "(surtout les 1 % low). " + XmpAdvice,
                details);
        }

        var configured = modules.Select(m => m.ConfiguredSpeedMts ?? 0).Max();
        var type = modules[0].SmbiosMemoryType;
        if (LooksLikeJedecDefault(type, configured))
        {
            return Result(DiagnosticStatus.Info,
                $"RAM à {Speed(configured)}, vitesse standard JEDEC.",
                "Windows indique que la RAM tourne à sa vitesse nominale, mais cette vitesse est une valeur standard " +
                "(JEDEC) souvent inférieure à celle annoncée pour les kits gaming. Si votre kit est vendu pour une " +
                "vitesse supérieure (voir sa référence ci-dessous ou sa fiche produit), le profil XMP/EXPO n'est " +
                "probablement pas activé. " + XmpAdvice,
                details);
        }

        return Result(DiagnosticStatus.Ok,
            $"RAM à {Speed(configured)}, vitesse nominale atteinte.",
            "Limite : la vitesse « nominale » rapportée par Windows est lue dans la barrette ; selon la carte mère, " +
            "elle correspond au profil XMP/EXPO ou à la vitesse standard JEDEC.",
            details);
    }

    /// <summary>Vitesses JEDEC basses, rarement celles d'un kit gaming. Volontairement prudent.</summary>
    private static bool LooksLikeJedecDefault(uint smbiosType, uint mts) => smbiosType switch
    {
        Ddr4 => mts is > 0 and <= 2666,
        Ddr5 => mts is > 0 and <= 4800,
        _ => false,
    };

    private static string Slot(MemoryModule m) => $"{m.BankLabel} {m.DeviceLocator}".Trim();

    private static string Speed(uint? mts) => mts is > 0 ? $"{mts} MT/s" : "inconnue";

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
