namespace OptiGame.Platform.Startup;

/// <summary>
/// Demande répétable depuis un terminal NON élevé (ex. memory.request : mesure de la mémoire écrite dans le journal) : comme
/// pour <see cref="QuitRequestWatcher"/>, il suffit de créer le fichier dans le dossier de données. Surveillance par événement
/// (FileSystemWatcher), sans polling ; les événements rapprochés d'une même création (Created puis Changed) comptent pour un.
/// </summary>
public sealed class RequestFileWatcher : IDisposable
{
    private static readonly TimeSpan SameRequest = TimeSpan.FromSeconds(2);

    private readonly FileSystemWatcher _watcher;
    private readonly Lock _gate = new();
    private long? _lastRequest;

    public RequestFileWatcher(string dataDirectory, string fileName, Action onRequested)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, fileName);
        File.Delete(path); // Demande périmée d'une exécution précédente.

        _watcher = new FileSystemWatcher(dataDirectory, fileName) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        FileSystemEventHandler handler = (_, _) =>
        {
            lock (_gate)
            {
                var now = Environment.TickCount64;
                if (_lastRequest is { } last && now - last < SameRequest.TotalMilliseconds) return;
                _lastRequest = now;
            }
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Encore ouvert par celui qui l'écrit : sans importance, il sera supprimé au prochain démarrage.
            }
            onRequested();
        };
        _watcher.Created += handler;
        _watcher.Changed += handler;
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose() => _watcher.Dispose();
}
