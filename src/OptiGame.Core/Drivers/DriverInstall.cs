using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace OptiGame.Core.Drivers;

public enum RestorePointAvailability
{
    /// <summary>Protection du système activée : un point de restauration peut être créé avant l'installation.</summary>
    Available,

    /// <summary>Protection du système désactivée (fréquent sous AtlasOS).</summary>
    ProtectionDisabled,

    /// <summary>État illisible : la clé qui l'indique n'est lisible qu'avec les droits administrateur.</summary>
    Unknown,
}

/// <summary>
/// Ce que la confirmation d'une installation de pilote affiche (principe 2 : quoi, pourquoi, admin, redémarrage).
/// Exception annoncée au principe 1 : l'installation d'un pilote ne passe pas par la sauvegarde des réglages d'OptiGame.
/// </summary>
public sealed record DriverInstallPlan(string Title, string What, string Why, bool MayRequireReboot, string NotReversible,
    string Rollback, RestorePointAvailability RestorePoint);

public static class DriverInstallPlans
{
    public const string NotReversible =
        "OptiGame ne peut pas annuler l'installation d'un pilote : contrairement à ses autres réglages, elle ne passe pas " +
        "par sa sauvegarde de l'état d'origine.";

    private const string DeviceManagerRollback =
        "Pour revenir au pilote précédent : Gestionnaire de périphériques → clic droit sur le périphérique → Propriétés → " +
        "onglet Pilote → « Restaurer le pilote » (Windows conserve l'ancienne version).";

    public static DriverInstallPlan ForNvidia(GpuDriverStatus status, NvidiaDriver latest, RestorePointAvailability restorePoint) => new(
        Title: $"Installer le pilote NVIDIA {latest.Version}",
        What: $"{status.GpuName}\n{status.InstalledVersion ?? "?"} → {latest.Version} ({latest.Name}" +
              (latest.ReleaseDate is { } date ? $", {date:dd/MM/yyyy}" : "") + ")\n" +
              $"Téléchargement : {latest.DownloadUrl.Host}{(latest.SizeText is { } size ? $" ({FrenchSize(size)})" : "")}\n" +
              "Puis ouverture de l'installeur officiel de NVIDIA : vous y choisissez l'installation express ou personnalisée.",
        Why: "Les nouveaux pilotes apportent des optimisations pour les jeux récents et des corrections de bugs. OptiGame " +
             "télécharge l'installeur depuis le site de NVIDIA et vérifie sa signature (NVIDIA Corporation) avant de l'ouvrir.",
        MayRequireReboot: true,
        NotReversible: NotReversible,
        Rollback: DeviceManagerRollback + " Vous pouvez aussi réinstaller l'ancienne version depuis nvidia.com.",
        RestorePoint: restorePoint);

    public static DriverInstallPlan ForWindowsUpdate(IReadOnlyList<WindowsUpdateDriver> drivers, RestorePointAvailability restorePoint) => new(
        Title: drivers.Count == 1 ? "Installer 1 pilote de Windows Update" : $"Installer {drivers.Count} pilotes de Windows Update",
        What: string.Join("\n", drivers.Select(d => d.Title)),
        Why: "Pilotes publiés par les fabricants dans le catalogue de Windows Update. Ils sont téléchargés et installés par " +
             "Windows Update lui-même, comme depuis les Paramètres de Windows.",
        MayRequireReboot: drivers.Any(d => d.MayRequireReboot),
        NotReversible: NotReversible,
        Rollback: DeviceManagerRollback,
        RestorePoint: restorePoint);

    /// <summary>« 990.85 MB » (format de NVIDIA) → « 990,85 Mo ».</summary>
    public static string FrenchSize(string size) => size.Replace("MB", "Mo").Replace("GB", "Go").Replace('.', ',');

    public static string RestorePointText(RestorePointAvailability availability) => availability switch
    {
        RestorePointAvailability.Available => "Créer d'abord un point de restauration du système (recommandé).",
        RestorePointAvailability.Unknown => "Impossible de vérifier si la protection du système est activée (droits administrateur " +
                                            "nécessaires) : aucun point de restauration ne sera créé. Le retour en arrière passe par " +
                                            "le Gestionnaire de périphériques.",
        _ => "La protection du système est désactivée sur ce PC (fréquent sous AtlasOS) : aucun point de restauration ne " +
             "peut être créé. Le retour en arrière passe par le Gestionnaire de périphériques.",
    };
}

public static partial class InstallerFiles
{
    /// <summary>
    /// Nom local de l'installeur : dernier segment de l'adresse officielle, lettres, chiffres, point, tiret et soulignés
    /// seulement, terminé par .exe. Null sinon (jamais de chemin fabriqué à partir d'une adresse douteuse).
    /// </summary>
    public static string? FileNameFor(Uri url)
    {
        if (!NvidiaDrivers.IsOfficialDownload(url)) return null;
        var name = url.Segments.LastOrDefault()?.Trim('/') ?? "";
        return SafeName().IsMatch(name) ? name : null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,150}\.exe$")]
    private static partial Regex SafeName();
}

public static class InstallerSignature
{
    /// <summary>
    /// Signataire attendu de l'installeur NVIDIA : CN et O « NVIDIA Corporation ». Vérifié sur l'installeur 617.14 le
    /// 2026-09-30 : « CN=NVIDIA Corporation, OU=2008B9F, O=NVIDIA Corporation, L=Santa Clara, S=California, C=US »
    /// (émis par DigiCert Trusted G4 Code Signing RSA4096 SHA384 2021 CA1). La validité de la signature elle-même est
    /// vérifiée à part (WinVerifyTrust).
    /// </summary>
    public static bool IsNvidia(string? signerSubject)
    {
        if (string.IsNullOrWhiteSpace(signerSubject)) return false;
        try
        {
            string? cn = null, o = null;
            foreach (var rdn in new X500DistinguishedName(signerSubject).EnumerateRelativeDistinguishedNames())
            {
                if (rdn.HasMultipleElements) continue;
                switch (rdn.GetSingleElementType().Value)
                {
                    case "2.5.4.3": cn = rdn.GetSingleElementValue(); break;
                    case "2.5.4.10": o = rdn.GetSingleElementValue(); break;
                }
            }
            return cn == "NVIDIA Corporation" && o == "NVIDIA Corporation";
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }
}

/// <summary>Résultat d'une installation Windows Update, par pilote (codes OperationResultCode de l'API).</summary>
public sealed record WindowsUpdateInstallResult(string Title, int ResultCode)
{
    public bool Succeeded => ResultCode is 2 or 3;

    public string Describe() => ResultCode switch
    {
        2 => "installé",
        3 => "installé avec des avertissements",
        4 => "échec",
        5 => "annulé",
        1 => "en cours",
        _ => "non installé",
    };
}

public sealed record WindowsUpdateInstallReport(IReadOnlyList<WindowsUpdateInstallResult> Results, bool RebootRequired, IReadOnlyList<string> Skipped);

public sealed record RestorePointReport(bool Created, string Message);
