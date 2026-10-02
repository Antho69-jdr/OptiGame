using OptiGame.Core.Logging;
using OptiGame.Core.Measurement;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;

namespace OptiGame.Platform.Measurement;

/// <summary>
/// Mesure automatique des FPS pendant les parties, pour la note des jeux : une seule capture PresentMon par partie, de
/// 60 s, après 4 min de jeu (menus et chargements passés). Session ETW distincte de celle de la page « Mesures » (une
/// capture manuelle n'est jamais interrompue). Annulée si la partie se termine avant. Seules les 5 dernières captures
/// automatiques de chaque jeu sont gardées. Désactivable dans les paramètres ; sans PresentMon, rien n'est fait.
/// </summary>
public sealed class AutoCapture(GameSessionManager sessions, CaptureStore store, PresentMonRunner runner, AppSettingsStore settings,
    FileLog log, TimeProvider time)
{
    public const int DelaySeconds = 240;
    public const int DurationSeconds = 60;
    public const int KeptPerGame = 5;

    private readonly Lock _lock = new();
    private CancellationTokenSource? _current;
    private bool _started;

    /// <summary>Une capture automatique vient d'être enregistrée (la note du jeu peut changer).</summary>
    public event EventHandler<CaptureRecord>? CaptureAdded;

    public void Start()
    {
        if (_started) return;
        _started = true;
        sessions.SessionStarted += (_, _) => OnSessionStarted();
        sessions.SessionEnded += (_, _) => Cancel();
    }

    private void OnSessionStarted()
    {
        if (sessions.Current is not { } session || !settings.Get().AutoMeasureFps) return;
        var presentMon = settings.Get().PresentMonPath is { } configured && File.Exists(configured) ? configured : runner.FindInToolsDirectory();
        if (presentMon is null)
        {
            log.Info("Mesure automatique ignorée : PresentMon introuvable (page « Mesures »).");
            return;
        }

        var cancellation = new CancellationTokenSource();
        lock (_lock)
        {
            _current?.Cancel();
            _current = cancellation;
        }
        _ = Task.Run(() => CaptureAsync(session, presentMon, cancellation.Token));
    }

    private void Cancel()
    {
        lock (_lock)
        {
            _current?.Cancel();
            _current = null;
        }
    }

    private async Task CaptureAsync(ActiveSession session, string presentMon, CancellationToken cancellation)
    {
        var exe = Path.GetFileName(session.Profile.ExePath);
        var now = time.GetLocalNow();
        var csvName = $"auto_{now:yyyyMMdd-HHmmss}_{Path.GetFileNameWithoutExtension(exe)}.csv";
        var request = new CaptureRequest(presentMon, exe, DurationSeconds, DelaySeconds, Path.Combine(store.Directory, csvName), CaptureRequest.AutoSessionName);
        try
        {
            var output = await runner.RunAsync(request, cancellation);
            var record = CaptureReader.Build(request, "Automatique", csvName, now.AddSeconds(DelaySeconds), output, session.Profile.Name, automatic: true);
            store.Add(record);
            log.Info($"Mesure automatique de « {session.Profile.Name} » : {record.Stats.AverageFps:0} FPS moyens, 1 % low {record.Stats.OnePercentLowFps:0} ({record.Stats.FrameCount} images).");
            Prune(exe);
            CaptureAdded?.Invoke(this, record);
        }
        catch (OperationCanceledException)
        {
            TryDelete(request.OutputCsv); // partie terminée avant la fin de la mesure : rien d'exploitable
            log.Info($"Mesure automatique de « {session.Profile.Name} » annulée (partie terminée avant).");
        }
        catch (FormatException ex)
        {
            // Fichier produit mais non reconnu : GARDÉ pour analyse, comme pour une capture manuelle.
            var kept = Path.ChangeExtension(request.OutputCsv, null) + "-non-reconnu.csv";
            if (File.Exists(request.OutputCsv)) File.Move(request.OutputCsv, kept, overwrite: true);
            log.Error($"Mesure automatique : CSV non reconnu, conservé dans {kept}", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            log.Error($"Mesure automatique de « {session.Profile.Name} » impossible", ex);
        }
    }

    /// <summary>Garde les 5 dernières captures automatiques de ce jeu (les captures manuelles ne sont jamais touchées).</summary>
    private void Prune(string exe)
    {
        var old = store.GetAll()
            .Where(c => c.Automatic && c.ProcessName.Equals(exe, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.CapturedAt)
            .Skip(KeptPerGame)
            .ToList();
        foreach (var capture in old) store.Remove(capture.Id);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex)
        {
            log.Warn($"Suppression impossible de {path} : {ex.Message}");
        }
    }
}
