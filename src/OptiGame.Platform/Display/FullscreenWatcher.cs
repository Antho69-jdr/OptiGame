using System.Runtime.InteropServices;
using System.Text;
using OptiGame.Core.Dock;

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

    private const uint MonitorDefaultToPrimary = 1;
    private const int GwlStyle = -16;
    private const int WsCaption = 0x00C00000;
    private const int GwlExStyle = -20;
    private const int WsExTopmost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;
    private const uint GwHwndPrev = 3;
    private const int DwmwaCloaked = 14;

    private readonly WinEventDelegate _callback; // gardé en champ : sinon le GC le libère et Windows appelle un pointeur mort
    private IntPtr _hook;

    public FullscreenWatcher()
    {
        _callback = (_, _, _, _, _, _, _) => Evaluate();
    }

    public bool IsFullscreen { get; private set; }

    public event EventHandler<bool>? FullscreenChanged;

    /// <summary>Bureau affiché par « Afficher le bureau » (Win+D) : aucune application au-dessus de lui.</summary>
    public bool IsDesktopShown { get; private set; }

    public event EventHandler<bool>? DesktopShownChanged;

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
        var desktopShown = DetectDesktopShown();
        if (desktopShown != IsDesktopShown)
        {
            IsDesktopShown = desktopShown;
            DesktopShownChanged?.Invoke(this, desktopShown);
        }

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

    /// <summary>Relevé Win32 ; la décision est dans Core (<see cref="FullscreenRules"/>, testée).</summary>
    private static bool DetectFullscreen()
    {
        var state = SHQueryUserNotificationState(out var s) == 0 ? s : 0;
        return FullscreenRules.ShouldHideDock(state, ReadForeground());
    }

    /// <summary>Relevé des fenêtres au-dessus du bureau au premier plan ; décision dans Core (<see cref="DesktopRules"/>, testée).</summary>
    private static bool DetectDesktopShown()
    {
        var desktop = GetForegroundWindow();
        if (desktop == IntPtr.Zero || ClassName(desktop) is not ("Progman" or "WorkerW")) return false;

        var above = new List<WindowAbove>();
        for (var window = GetWindow(desktop, GwHwndPrev); window != IntPtr.Zero; window = GetWindow(window, GwHwndPrev))
        {
            var exStyle = GetWindowLong(window, GwlExStyle);
            var cloaked = DwmGetWindowAttribute(window, DwmwaCloaked, out var value, sizeof(int)) == 0 && value != 0;
            var hasArea = GetWindowRect(window, out var r) && r.Right > r.Left && r.Bottom > r.Top;
            above.Add(new WindowAbove(IsWindowVisible(window), cloaked, IsIconic(window),
                (exStyle & WsExTopmost) != 0, (exStyle & WsExToolWindow) != 0, hasArea));
        }
        return DesktopRules.IsDesktopShown(desktopIsForeground: true, above);
    }

    private static string ClassName(IntPtr window)
    {
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private static ForegroundWindowInfo? ReadForeground()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !GetWindowRect(window, out var rect)) return null;

        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return null;

        // Le dock est sur l'écran principal (celui qui contient le point 0,0).
        var dockMonitor = MonitorFromPoint(new Point { X = 0, Y = 0 }, MonitorDefaultToPrimary);
        var style = GetWindowLong(window, GwlStyle);
        var m = info.rcMonitor;
        return new ForegroundWindowInfo(
            IsShell: IsShellWindow(window),
            OnDockMonitor: monitor == dockMonitor,
            IsMaximized: IsZoomed(window),
            HasCaption: (style & WsCaption) == WsCaption,
            Window: new ScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
            Monitor: new ScreenRect(m.Left, m.Top, m.Right, m.Bottom));
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int max);
}
