using System.Runtime.InteropServices;
using System.Text;

namespace OptiGame.Platform.Display;

/// <summary>
/// Indique si une application occupe tout l'écran (jeu, vidéo, présentation), pour masquer le dock.
/// Événementiel : abonnement à EVENT_SYSTEM_FOREGROUND (changement de fenêtre au premier plan), aucun polling.
/// <see cref="Evaluate"/> peut aussi être appelé à la demande (ex. avant d'afficher le dock).
/// Doit être démarré sur un thread avec boucle de messages (thread UI).
/// </summary>
public sealed class FullscreenWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const uint MonitorDefaultToNearest = 2;

    // QUERY_USER_NOTIFICATION_STATE : 2 = application plein écran, 3 = Direct3D plein écran, 4 = présentation.
    private const int QunsBusy = 2;
    private const int QunsRunningD3dFullScreen = 3;
    private const int QunsPresentationMode = 4;

    private readonly WinEventDelegate _callback; // gardé en champ : sinon le GC le libère et Windows appelle un pointeur mort
    private IntPtr _hook;

    public FullscreenWatcher()
    {
        _callback = (_, _, _, _, _, _, _) => Evaluate();
    }

    public bool IsFullscreen { get; private set; }

    public event EventHandler<bool>? FullscreenChanged;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0,
            WinEventOutOfContext | WinEventSkipOwnProcess);
        Evaluate();
    }

    /// <summary>Réévalue l'état et prévient en cas de changement. Renvoie l'état courant.</summary>
    public bool Evaluate()
    {
        var fullscreen = DetectFullscreen();
        if (fullscreen != IsFullscreen)
        {
            IsFullscreen = fullscreen;
            FullscreenChanged?.Invoke(this, fullscreen);
        }
        return fullscreen;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private static bool DetectFullscreen()
    {
        if (SHQueryUserNotificationState(out var state) == 0 &&
            state is QunsBusy or QunsRunningD3dFullScreen or QunsPresentationMode)
        {
            return true;
        }

        // Jeux en « plein écran fenêtré » (fenêtre sans bordure à la taille de l'écran) : non signalés par l'API ci-dessus.
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || IsShellWindow(window) || !GetWindowRect(window, out var rect)) return false;

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref info)) return false;

        var m = info.rcMonitor;
        return rect.Left <= m.Left && rect.Top <= m.Top && rect.Right >= m.Right && rect.Bottom >= m.Bottom;
    }

    /// <summary>Bureau et barre des tâches : couvrent l'écran mais ne sont pas des applications plein écran.</summary>
    private static bool IsShellWindow(IntPtr window)
    {
        if (window == GetShellWindow() || window == GetDesktopWindow()) return true;
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int max);
}
