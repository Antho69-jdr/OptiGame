using OptiGame.Core.Logging;

namespace OptiGame.Platform.Library;

/// <summary>
/// Surveille les manifestes des bibliothèques Steam (steamapps\appmanifest_*.acf) : Steam en crée un au début d'une
/// installation et le réécrit à la fin. FileSystemWatcher = notification de Windows, aucune scrutation ; un seul
/// signal, 3 s après la dernière écriture (une installation écrit le manifeste de nombreuses fois).
/// </summary>
public sealed class SteamLibraryWatcher(FileLog log) : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);

    private readonly Lock _lock = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _timer;

    public event EventHandler? ManifestsChanged;

    public void Start()
    {
        lock (_lock)
        {
            if (_watchers.Count > 0) return;
            foreach (var library in GameLibraryScanner.SteamLibraries())
            {
                var steamapps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(steamapps)) continue;
                try
                {
                    var watcher = new FileSystemWatcher(steamapps, "appmanifest_*.acf")
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    };
                    watcher.Changed += (_, _) => Schedule();
                    watcher.Created += (_, _) => Schedule();
                    watcher.Renamed += (_, _) => Schedule();
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    log.Warn($"Bibliothèque Steam non surveillée ({steamapps}) : {ex.Message}");
                }
            }
            log.Info($"Nouveaux jeux Steam : {_watchers.Count} bibliothèque(s) surveillée(s).");
        }
    }

    private void Schedule()
    {
        lock (_lock)
        {
            _timer ??= new Timer(_ => ManifestsChanged?.Invoke(this, EventArgs.Empty));
            _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
            _timer?.Dispose();
            _timer = null;
        }
    }
}
