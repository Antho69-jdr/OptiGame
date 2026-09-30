namespace OptiGame.Platform.Startup;

/// <summary>
/// Demande d'arrÃªt propre depuis un terminal NON Ã©levÃ© (scripts\dev-run.ps1) : il ne peut pas arrÃªter OptiGame, qui
/// tourne en administrateur, mais il peut crÃ©er un fichier dans le dossier de donnÃ©es de l'utilisateur.
/// Surveillance par Ã©vÃ©nement (FileSystemWatcher), sans polling.
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
        File.Delete(_path); // Demande pÃ©rimÃ©e d'une exÃ©cution prÃ©cÃ©dente.

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
                // Le script l'Ã©crit peut-Ãªtre encore : sans importance, il sera supprimÃ© au prochain dÃ©marrage.
            }
            onQuitRequested();
        };
        _watcher.Created += handler;
        _watcher.Changed += handler;
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose() => _watcher.Dispose();
}

