namespace OptiGame.Core.Library;

public enum DiskKind
{
    Ssd,
    Hdd,
    Unknown,
}

public enum GameDiskLevel
{
    Ok,
    Info,
    Warning,
}

/// <summary>
/// Ce que Windows dit du disque d'un jeu. MediaType et BusType : valeurs de MSFT_PhysicalDisk (MediaType 3 = HDD, 4 = SSD,
/// 5 = SCM, 0 = non précisé ; BusType 7 = USB, 11 = SATA, 17 = NVMe). SeekPenalty / Trim : réponses du disque lui-même
/// (IOCTL_STORAGE_QUERY_PROPERTY), null s'il ne répond pas.
/// </summary>
public sealed record DiskFacts(string Drive, string? Model, int? MediaType, int? BusType, bool? SeekPenalty, bool? Trim,
    long FreeBytes, long TotalBytes, bool IsNetwork = false);

public sealed record GameDiskReport(GameDiskLevel Level, DiskKind Kind, string Summary, string Detail, IReadOnlyList<string> Advice);

/// <summary>
/// Disque d'installation d'un jeu : type (SSD ou disque dur) et espace libre.
/// Sur la machine de dev (2026-10-02), le disque dur WD10EARX est déclaré « non précisé » (MediaType 0) par Windows et ne
/// répond pas à la question de la pénalité de recherche (erreur 31) ; seule la réponse TRIM (non prise en charge) le révèle.
/// D'où l'ordre : type déclaré, puis pénalité de recherche, puis TRIM. Un disque USB 2.0 ne répond à aucune des deux.
/// </summary>
public static class GameDisk
{
    public const int UsbBus = 7;
    public const int NvmeBus = 17;

    /// <summary>Espace libre minimal conseillé : mises à jour (souvent téléchargées puis décompressées), cache des shaders.</summary>
    public const long LowFreeBytes = 20L * 1024 * 1024 * 1024;

    public static DiskKind KindOf(DiskFacts disk) => disk.MediaType switch
    {
        3 => DiskKind.Hdd,
        4 or 5 => DiskKind.Ssd,
        _ => disk.SeekPenalty switch
        {
            true => DiskKind.Hdd,
            false => DiskKind.Ssd,
            null => disk.Trim switch
            {
                true => DiskKind.Ssd,
                false => DiskKind.Hdd, // pas de TRIM : propre aux disques durs
                null => DiskKind.Unknown,
            },
        },
    };

    public static GameDiskReport Assess(DiskFacts disk, bool isSteamGame)
    {
        var kind = disk.IsNetwork ? DiskKind.Unknown : KindOf(disk);
        var free = FormatSize(disk.FreeBytes);
        var total = FormatSize(disk.TotalBytes);
        var type = disk.IsNetwork ? "Lecteur réseau"
            : kind switch
            {
                DiskKind.Ssd => disk.BusType == NvmeBus ? "SSD NVMe" : "SSD",
                DiskKind.Hdd => "Disque dur",
                _ => disk.BusType == UsbBus ? "Disque externe USB (type inconnu)" : "Type de disque inconnu",
            };
        var detail = $"{type} {disk.Drive}{(disk.Model is { Length: > 0 } m ? $" ({m})" : "")} · {free} libres sur {total}";

        var advice = new List<string>();
        var level = GameDiskLevel.Ok;
        var problems = new List<string>();
        if (kind == DiskKind.Hdd)
        {
            level = GameDiskLevel.Warning;
            problems.Add("installé sur un disque dur");
            advice.Add("Un disque dur rallonge les chargements et provoque des saccades quand le jeu charge des zones ou des textures " +
                       "en cours de partie ; les jeux récents (Star Citizen, Cyberpunk 2077…) exigent un SSD.");
            advice.Add(isSteamGame
                ? "Pour le déplacer sur un SSD : Steam → clic droit sur le jeu → Propriétés → Fichiers installés → Déplacer le dossier d'installation."
                : "Pour le déplacer sur un SSD : utilisez l'option de déplacement de son lanceur, ou réinstallez-le sur un SSD.");
        }
        else if (disk.IsNetwork || disk.BusType == UsbBus)
        {
            level = GameDiskLevel.Info;
            advice.Add(disk.IsNetwork
                ? "Un jeu lancé depuis un lecteur réseau dépend du débit du réseau : chargements lents et saccades possibles."
                : "Selon la connexion (USB 2.0 notamment) et le disque, un disque externe peut ralentir les chargements.");
        }

        if (!disk.IsNetwork && disk.TotalBytes > 0 && (disk.FreeBytes < LowFreeBytes || disk.FreeBytes < disk.TotalBytes / 10))
        {
            level = GameDiskLevel.Warning;
            problems.Add("disque presque plein");
            advice.Add("Gardez au moins 20 Go (et 10 % du disque) libres : les mises à jour sont souvent téléchargées puis décompressées, " +
                       "et le cache des shaders grossit avec les parties. Un SSD presque plein ralentit aussi.");
        }

        var summary = problems.Count > 0 ? char.ToUpperInvariant(problems[0][0]) + string.Join(" et ", problems)[1..] + "."
            : level == GameDiskLevel.Info ? "À surveiller." : "Bon emplacement.";
        return new GameDiskReport(level, kind, summary, detail, advice);
    }

    /// <summary>« 192 Go », « 1,4 To ».</summary>
    public static string FormatSize(long bytes)
    {
        const double gb = 1024d * 1024 * 1024;
        var value = bytes / gb;
        return value >= 1000 ? $"{value / 1024:0.#} To".Replace('.', ',') : $"{value:0} Go";
    }
}
