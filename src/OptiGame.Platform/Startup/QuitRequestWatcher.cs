namespace OptiGame.Platform.Startup;

/// <summary>
/// Demande d'arrêt propre par un fichier du dossier de données : scripts\dev-run.ps1 (terminal NON élevé, qui ne peut pas
/// arrêter OptiGame, en administrateur, mais peut créer ce fichier) et l'installeur (mise à jour, désinstallation).
/// Surveillance par événement (FileSystemWatcher), sans polling.
/// </summary>
public sealed class QuitRequestWatcher : IDisposable
{
    public const string FileName = "quit.request";

    private readonly FileSystemWatcher _watcher;
    private readonly string _path;
    private int _requested;

    public QuitRequestWatcher(string dataDirectory, Action onQuitRequested)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, FileName);
        File.Delete(_path); // Demande périmée d'une exécution précédente.

        _watcher = new FileSystemWatcher(dataDirectory, FileName) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        FileSystemEventHandler handler = (_, _) =>
        {
            if (Interlocked.Exchange(ref _requested, 1) == 1) return;
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
                // Le script l'écrit peut-être encore : sans importance, il sera supprimé au prochain démarrage.
            }
            onQuitRequested();
        };
        _watcher.Created += handler;
        _watcher.Changed += handler;
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose() => _watcher.Dispose();
}

