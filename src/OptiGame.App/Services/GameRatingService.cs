using OptiGame.Core.Abstractions;
using OptiGame.Core.Logging;
using OptiGame.Core.Measurement;
using OptiGame.Core.Profiles;
using OptiGame.Core.Rating;
using OptiGame.Platform.Library;

namespace OptiGame.App.Services;

/// <summary>
/// Note des jeux : caractéristiques du PC (lues une fois), configuration requise (Steam, sinon PCGamingWiki ; en cache), captures du jeu
/// (manuelles et automatiques). Le calcul lui-même est dans Core (<see cref="GameRatings"/>).
/// </summary>
public sealed class GameRatingService(
    IGpuInfoProvider gpus,
    IMemoryInfoProvider memory,
    IDisplayInfoProvider displays,
    CaptureStore captures,
    GameRequirementsClient requirements,
    FileLog log)
{
    private PcSpecs? _pc;

    /// <summary>
    /// Le PC : carte graphique reconnue et mémoire (WMI, lues une fois), écran de jeu = l'écran principal (relu à chaque fois :
    /// résolution ou fréquence changées depuis le démarrage). Hors du thread UI.
    /// </summary>
    public PcSpecs Pc()
    {
        if (_pc is null)
        {
            var adapters = gpus.GetAdapters().Where(g => g.IsPhysical).ToList();
            var identified = adapters.Select(a => (Adapter: a, Match: GpuPerformance.Identify(a.Name))).Where(x => x.Match is not null)
                .OrderByDescending(x => x.Match!.Index).FirstOrDefault();
            var memoryGb = (int)Math.Round(memory.GetMemoryInfo().Modules.Sum(m => (double)m.CapacityBytes) / (1024d * 1024 * 1024));
            _pc = new PcSpecs(identified.Adapter?.Name ?? adapters.FirstOrDefault()?.Name ?? "?", identified.Match, memoryGb, 0, 0, 0);
        }
        var screen = GameRatings.GamingDisplay(displays.GetDisplays());
        var pc = _pc with { Width = screen?.Width ?? 1920, Height = screen?.Height ?? 1080, RefreshHz = screen?.CurrentHz ?? 60 };
        if (pc != _logged)
        {
            _logged = pc;
            log.Info($"Note des jeux : PC = {pc.GpuName} (indice {pc.Gpu?.Index.ToString() ?? "inconnu"}), {pc.MemoryGb} Go, écran principal " +
                     $"{pc.Width}×{pc.Height} à {pc.RefreshHz} Hz.");
        }
        return pc;
    }

    private PcSpecs? _logged;

    /// <summary>Note d'un jeu (null si ni configuration requise reconnue ni mesure). Réseau, WMI et fichiers : hors du thread UI.</summary>
    public async Task<GameRating?> RateAsync(GameProfile profile, IReadOnlyList<GameLibraryScanner.SteamApp> steamApps)
    {
        var pc = Pc();
        GameRatings.Estimate? estimate = null;
        if (GameLibraryScanner.SteamAppIdFor(profile, steamApps) is { } appId && await requirements.GetAsync(appId) is { } needs)
        {
            estimate = GameRatings.EstimateFrom(pc, needs);
        }
        // Jeu hors Steam, ou cartes citées par Steam inconnues de la table : PCGamingWiki, d'après le nom du jeu.
        if (estimate is null && await requirements.FindByNameAsync(profile.Name) is { } wiki)
        {
            estimate = GameRatings.EstimateFrom(pc, wiki);
        }

        var exe = Path.GetFileName(profile.ExePath);
        var measured = captures.GetAll()
            .Where(c => c.ProcessName.Equals(exe, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.CapturedAt)
            .Select(c => new GameRatings.MeasuredCapture(c.Stats, c.Load ?? LoadOf(c), c.Preset, c.GpuHealth))
            .ToList();
        return GameRatings.Combine(estimate, GameRatings.MeasureFrom(measured, pc.RefreshHz, profile.GraphicsPreset, pc));
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
