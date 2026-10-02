using OptiGame.Core.Abstractions;
using OptiGame.Core.Logging;
using OptiGame.Core.Measurement;
using OptiGame.Core.Profiles;
using OptiGame.Core.Rating;
using OptiGame.Platform.Library;

namespace OptiGame.App.Services;

/// <summary>
/// Note des jeux : caractéristiques du PC (lues une fois), configuration requise Steam (en cache), captures du jeu
/// (manuelles et automatiques). Le calcul lui-même est dans Core (<see cref="GameRatings"/>).
/// </summary>
public sealed class GameRatingService(
    IGpuInfoProvider gpus,
    IMemoryInfoProvider memory,
    IDisplayInfoProvider displays,
    CaptureStore captures,
    SteamRequirementsClient requirements,
    FileLog log)
{
    private PcSpecs? _pc;

    /// <summary>Le PC : carte graphique reconnue, mémoire, et écran de jeu (celui à la plus haute fréquence). Hors du thread UI.</summary>
    public PcSpecs Pc()
    {
        if (_pc is not null) return _pc;
        var adapters = gpus.GetAdapters().Where(g => g.IsPhysical).ToList();
        var identified = adapters.Select(a => (Adapter: a, Match: GpuPerformance.Identify(a.Name))).Where(x => x.Match is not null)
            .OrderByDescending(x => x.Match!.Index).FirstOrDefault();
        var memoryGb = (int)Math.Round(memory.GetMemoryInfo().Modules.Sum(m => (double)m.CapacityBytes) / (1024d * 1024 * 1024));
        var screen = displays.GetDisplays().OrderByDescending(d => d.CurrentHz).ThenByDescending(d => d.Width * d.Height).FirstOrDefault();
        _pc = new PcSpecs(identified.Adapter?.Name ?? adapters.FirstOrDefault()?.Name ?? "?", identified.Match, memoryGb,
            screen?.Width ?? 1920, screen?.Height ?? 1080, screen?.CurrentHz ?? 60);
        log.Info($"Note des jeux : PC = {_pc.GpuName} (indice {_pc.Gpu?.Index.ToString() ?? "inconnu"}), {memoryGb} Go, écran {_pc.Width}×{_pc.Height} à {_pc.RefreshHz} Hz.");
        return _pc;
    }

    /// <summary>Note d'un jeu (null si ni configuration requise reconnue ni mesure). Réseau, WMI et fichiers : hors du thread UI.</summary>
    public async Task<GameRating?> RateAsync(GameProfile profile, IReadOnlyList<GameLibraryScanner.SteamApp> steamApps)
    {
        var pc = Pc();
        GameRatings.Estimate? estimate = null;
        if (GameLibraryScanner.SteamAppIdFor(profile, steamApps) is { } appId && await requirements.GetAsync(appId) is { } needs)
        {
            estimate = GameRatings.EstimateFrom(pc, needs);
        }

        var exe = Path.GetFileName(profile.ExePath);
        var measured = captures.GetAll()
            .Where(c => c.ProcessName.Equals(exe, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.CapturedAt)
            .Select(c => new GameRatings.MeasuredCapture(c.Stats, c.Load ?? LoadOf(c), c.Preset))
            .ToList();
        return GameRatings.Combine(estimate, GameRatings.MeasureFrom(measured, pc.RefreshHz, profile.GraphicsPreset));
    }

    private readonly HashSet<Guid> _loadTried = [];

    /// <summary>
    /// Charge d'une capture antérieure à sa prise en compte : recalculée une fois depuis son CSV, puis enregistrée.
    /// Un CSV sans ces colonnes ou illisible n'est relu qu'une fois par lancement (et n'est jamais supprimé).
    /// </summary>
    private FrameLoad? LoadOf(CaptureRecord capture)
    {
        lock (_loadTried)
        {
            if (!_loadTried.Add(capture.Id)) return null;
        }
        try
        {
            using var reader = new StreamReader(captures.CsvPath(capture));
            var load = FrameLoad.Compute(PresentMonCsv.MainSwapChain(PresentMonCsv.Parse(reader)));
            if (load is not null) captures.SetLoad(capture.Id, load);
            return load;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            log.Warn($"Charge de la capture « {capture.Label} » illisible : {ex.Message}");
            return null;
        }
    }
}
