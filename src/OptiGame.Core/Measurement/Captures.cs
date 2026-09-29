using System.Globalization;
using OptiGame.Core.State;

namespace OptiGame.Core.Measurement;

/// <summary>Paramètres d'une capture PresentMon.</summary>
public sealed record CaptureRequest(string PresentMonPath, string ProcessName, int DurationSeconds, int DelaySeconds, string OutputCsv)
{
    public const string SessionName = "OptiGame_Capture";

    /// <summary>
    /// Arguments PresentMon 2.x (options vérifiées avec « PresentMon-2.6.0-x64.exe --help ») :
    /// capture limitée au processus du jeu, durée fixe puis arrêt, métriques 2.x, pas d'affichage console,
    /// session nommée (et remplacée si une capture précédente a été interrompue).
    /// </summary>
    public IReadOnlyList<string> BuildArguments()
    {
        var args = new List<string>
        {
            "--process_name", ProcessName,
            "--output_file", OutputCsv,
            "--timed", DurationSeconds.ToString(CultureInfo.InvariantCulture),
            "--terminate_after_timed",
            "--v2_metrics",
            "--no_console_stats",
            "--session_name", SessionName,
            "--stop_existing_session",
        };
        if (DelaySeconds > 0)
        {
            args.AddRange(["--delay", DelaySeconds.ToString(CultureInfo.InvariantCulture)]);
        }
        return args;
    }
}

public sealed class CaptureRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Libellé libre, ex. « Avant optimisation ».</summary>
    public required string Label { get; set; }

    public required string ProcessName { get; set; }

    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>Profil OptiGame actif pendant la capture (null = aucun).</summary>
    public string? ActiveProfile { get; set; }

    /// <summary>Nom du fichier CSV, relatif au dossier des captures.</summary>
    public required string CsvFile { get; set; }

    public required FrameStats Stats { get; set; }
}

public sealed class CapturesDocument
{
    public int Version { get; set; } = 1;

    public List<CaptureRecord> Captures { get; set; } = [];
}

/// <summary>Index des captures (captures\captures.json) ; les CSV sont à côté.</summary>
public sealed class CaptureStore(IStateStore<CapturesDocument> store, string capturesDirectory)
{
    private readonly Lock _lock = new();
    private CapturesDocument _document = store.Load() ?? new CapturesDocument();

    public string Directory { get; } = capturesDirectory;

    public IReadOnlyList<CaptureRecord> GetAll()
    {
        lock (_lock) return _document.Captures.OrderByDescending(c => c.CapturedAt).ToList();
    }

    public void Add(CaptureRecord record)
    {
        lock (_lock)
        {
            _document.Captures.Add(record);
            store.Save(_document);
        }
    }

    public void Rename(Guid id, string label)
    {
        lock (_lock)
        {
            if (_document.Captures.FirstOrDefault(c => c.Id == id) is not { } record) return;
            record.Label = label;
            store.Save(_document);
        }
    }

    /// <summary>Supprime l'entrée et son fichier CSV.</summary>
    public void Remove(Guid id)
    {
        lock (_lock)
        {
            if (_document.Captures.FirstOrDefault(c => c.Id == id) is not { } record) return;
            _document.Captures.Remove(record);
            store.Save(_document);
            File.Delete(CsvPath(record));
        }
    }

    public string CsvPath(CaptureRecord record) => Path.Combine(Directory, record.CsvFile);

    /// <summary>Frametimes de la chaîne principale, pour le graphe.</summary>
    public IReadOnlyList<double> LoadFrameTimes(CaptureRecord record)
    {
        using var reader = new StreamReader(CsvPath(record));
        return PresentMonCsv.MainSwapChain(PresentMonCsv.Parse(reader)).Select(f => f.MsBetweenPresents).ToList();
    }
}
