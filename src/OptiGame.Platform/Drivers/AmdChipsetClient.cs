using System.Management;
using OptiGame.Core.Drivers;
using OptiGame.Core.Logging;

namespace OptiGame.Platform.Drivers;

/// <summary>
/// Logiciel de chipset AMD : processeur et carte mère (WMI Win32_Processor / Win32_BaseBoard), paquet installé
/// (programmes installés), pilotes AMD (Win32_PnPSignedDriver), puis page de téléchargement d'AMD (lecture seule).
/// Vérifié sur la machine de dev le 2026-10-02 : Ryzen 5 3600, MSI B450M MORTAR MAX, paquet absent, 6 pilotes AMD.
/// </summary>
public sealed class AmdChipsetClient(FileLog log)
{
    private static readonly HttpClient Http = CreateClient();

    /// <summary>Null si le processeur n'est pas un AMD (rien à proposer). Lectures WMI et réseau : hors du thread UI.</summary>
    public async Task<ChipsetDriverStatus?> CheckAsync(CancellationToken cancellation = default)
    {
        var cpu = First("SELECT Name FROM Win32_Processor", "Name");
        if (cpu is null || !cpu.Contains("AMD", StringComparison.OrdinalIgnoreCase)) return null;

        var board = $"{First("SELECT Manufacturer, Product FROM Win32_BaseBoard", "Manufacturer")} {First("SELECT Product FROM Win32_BaseBoard", "Product")}".Trim();
        var amdBoard = AmdChipset.BoardFromProduct(First("SELECT Product FROM Win32_BaseBoard", "Product"));
        var description = $"{cpu.Trim()} · {board}" + (amdBoard is null ? "" : $" (chipset {amdBoard.Chipset.ToUpperInvariant()})");
        var package = InstalledPackageVersion();
        var drivers = AmdDrivers();

        if (amdBoard is null)
        {
            return AmdChipset.Evaluate(description, package, drivers, null, AmdChipset.GenericSupportPage,
                "Chipset de la carte mère non reconnu : choisissez-le sur le site d'AMD.");
        }

        var page = AmdChipset.SupportPage(amdBoard);
        try
        {
            var release = AmdChipset.ParsePage(await Http.GetStringAsync(page, cancellation));
            if (release is not null && !OfficialInstallers.IsOfficialDownload(InstallerVendor.Amd, release.DownloadUrl))
            {
                log.Warn($"Logiciel de chipset AMD ignoré : adresse non officielle ({release.DownloadUrl}).");
                release = null;
            }
            log.Info($"Chipset AMD ({description}) : paquet installé {package ?? "aucun"}, {drivers.Count} pilote(s) AMD, dernier publié {release?.Version ?? "introuvable"}.");
            return AmdChipset.Evaluate(description, package, drivers, release, page,
                release is null ? "Format de la page d'AMD non reconnu : vérifiez directement sur le site." : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.Error($"Page de chipset AMD injoignable ({page})", ex);
            return AmdChipset.Evaluate(description, package, drivers, null, page, $"Site d'AMD injoignable : {ex.Message}");
        }
    }

    /// <summary>Version du paquet « AMD Chipset Software » (programmes installés), ou null s'il n'est pas installé.</summary>
    private static string? InstalledPackageVersion()
    {
        foreach (var root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var uninstall = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root);
            if (uninstall is null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var app = uninstall.OpenSubKey(name);
                if (app?.GetValue("DisplayName") is string display && display.Contains("AMD Chipset Software", StringComparison.OrdinalIgnoreCase))
                {
                    return app.GetValue("DisplayVersion") as string;
                }
            }
        }
        return null;
    }

    private static List<AmdDriverInfo> AmdDrivers()
    {
        var drivers = new List<AmdDriverInfo>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceName, DriverVersion, DriverDate, DriverProviderName FROM Win32_PnPSignedDriver WHERE DriverProviderName LIKE '%Advanced Micro Devices%' OR DriverProviderName LIKE 'AMD%'");
        foreach (var driver in searcher.Get())
        {
            using (driver)
            {
                if (driver["DeviceName"] is not string name) continue;
                DateOnly? date = driver["DriverDate"] is string wmiDate && wmiDate.Length >= 8 &&
                                 DateOnly.TryParseExact(wmiDate[..8], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
                drivers.Add(new AmdDriverInfo(name, driver["DriverVersion"] as string, date));
            }
        }
        return drivers;
    }

    private static string? First(string query, string property)
    {
        using var searcher = new ManagementObjectSearcher(query);
        foreach (var item in searcher.Get())
        {
            using (item) return (item[property] as string)?.Trim();
        }
        return null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OptiGame/1.0");
        return client;
    }
}
