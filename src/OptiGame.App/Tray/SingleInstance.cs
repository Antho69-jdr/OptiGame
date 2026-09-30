namespace OptiGame.App.Tray;

/// <summary>
/// Garantit une seule instance par session utilisateur. Une seconde instance signale la première
/// (qui affiche sa fenêtre) puis se ferme. Aucune attente active : le signal est un handle noyau.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // Une instance de développement (OPTIGAME_DATA_DIR défini) a ses propres noms : elle peut tourner à côté
    // de l'instance normale sans la réveiller ni se fermer.
    private static readonly string Suffix =
        Environment.GetEnvironmentVariable("OPTIGAME_DATA_DIR") is { Length: > 0 } dev
            ? "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dev.ToUpperInvariant())))[..12]
            : "";

    private static readonly string MutexName = @"Local\OptiGame.SingleInstance" + Suffix;
    private static readonly string EventName = @"Local\OptiGame.Activate" + Suffix;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly RegisteredWaitHandle _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle activateEvent, Action onActivationRequested)
    {
        _mutex = mutex;
        _activateEvent = activateEvent;
        _registration = ThreadPool.RegisterWaitForSingleObject(
            activateEvent, (_, _) => onActivationRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Renvoie null si une autre instance existe déjà (après lui avoir demandé de s'afficher).</summary>
    public static SingleInstance? TryAcquire(Action onActivationRequested)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

        if (!createdNew)
        {
            activateEvent.Set();
            activateEvent.Dispose();
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex, activateEvent, onActivationRequested);
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _activateEvent.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
