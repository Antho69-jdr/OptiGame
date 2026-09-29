namespace OptiGame.App.Tray;

/// <summary>
/// Garantit une seule instance par session utilisateur. Une seconde instance signale la première
/// (qui affiche sa fenêtre) puis se ferme. Aucune attente active : le signal est un handle noyau.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\OptiGame.SingleInstance";
    private const string EventName = @"Local\OptiGame.Activate";

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
