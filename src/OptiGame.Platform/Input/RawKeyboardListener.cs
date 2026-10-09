using System.Runtime.InteropServices;
using System.Text;

namespace OptiGame.Platform.Input;

/// <summary>
/// Touches du clavier reçues même quand un jeu est au premier plan, par l'« entrée brute » de Windows (RegisterRawInputDevices,
/// RIDEV_INPUTSINK) sur une fenêtre de messages : ce n'est PAS un crochet clavier — aucune touche n'est retardée ni retirée au jeu,
/// et c'est le mécanisme que les jeux utilisent eux-mêmes. Thread dédié avec sa boucle de messages (indépendant de l'interface).
/// Démarré pendant les appels seulement. <see cref="KeyChanged"/> est levé sur ce thread.
/// </summary>
public sealed class RawKeyboardListener : IDisposable
{
    private const int WmInput = 0x00FF;
    private const int WmClose = 0x0010;
    private const int WmDestroy = 0x0002;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeKeyboard = 1;
    private const uint RideVInputSink = 0x00000100;
    private const uint RideVRemove = 0x00000001;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly WndProc _wndProc; // gardé en champ : sinon le GC le libère et Windows appelle un pointeur mort
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private IntPtr _hwnd;
    private Exception? _startError;

    public RawKeyboardListener() => _wndProc = OnMessage;

    /// <summary>(touche virtuelle, enfoncée) — sur le thread d'écoute.</summary>
    public event Action<int, bool>? KeyChanged;

    public bool IsRunning => _hwnd != IntPtr.Zero;

    public void Start()
    {
        if (_thread is not null) return;
        _started.Reset();
        _thread = new Thread(Run) { IsBackground = true, Name = "OptiGame — clavier (appel)" };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
        if (_startError is { } error)
        {
            _thread = null;
            throw new InvalidOperationException("Lecture du clavier impossible.", error);
        }
    }

    private void Run()
    {
        try
        {
            var className = "OptiGameRawKeyboard" + Environment.CurrentManagedThreadId;
            var windowClass = new WndClassEx
            {
                cbSize = Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = className,
            };
            if (RegisterClassEx(ref windowClass) == 0) throw new System.ComponentModel.Win32Exception();
            _hwnd = CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, windowClass.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            var device = new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RideVInputSink, Target = _hwnd };
            if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>())) throw new System.ComponentModel.Win32Exception();
        }
        catch (Exception ex)
        {
            _startError = ex;
            if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            _started.Set();
            return;
        }
        _started.Set();
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmInput:
                ReadKey(lParam);
                break; // DefWindowProc ci-dessous : nettoyage exigé par Windows pour WM_INPUT
            case WmClose:
                var remove = new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RideVRemove, Target = IntPtr.Zero };
                RegisterRawInputDevices([remove], 1, (uint)Marshal.SizeOf<RawInputDevice>());
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            case WmDestroy:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private unsafe void ReadKey(IntPtr rawInput)
    {
        var headerSize = (uint)sizeof(RawInputHeader);
        uint size = 0;
        GetRawInputData(rawInput, RidInput, IntPtr.Zero, ref size, headerSize);
        if (size == 0 || size > 256) return;
        var buffer = stackalloc byte[(int)size];
        if (GetRawInputData(rawInput, RidInput, (IntPtr)buffer, ref size, headerSize) != size) return;
        var header = (RawInputHeader*)buffer;
        if (header->Type != RimTypeKeyboard) return;
        var keyboard = (RawKeyboard*)(buffer + headerSize);
        if (keyboard->VKey is 0 or 0xFF) return; // partie d'une séquence d'échappement
        KeyChanged?.Invoke(keyboard->VKey, (keyboard->Flags & 1) == 0);
    }

    /// <summary>Nom de la touche selon la disposition du clavier (« ù », « ^ ») ; null s'il n'y en a pas.</summary>
    public static string? KeyName(int virtualKey)
    {
        var scanCode = MapVirtualKey((uint)virtualKey, 0);
        if (scanCode == 0) return null;
        var text = new StringBuilder(64);
        return GetKeyNameText((int)(scanCode << 16), text, text.Capacity) > 0 ? text.ToString() : null;
    }

    public void Dispose()
    {
        if (_thread is null) return;
        if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _hwnd = IntPtr.Zero;
        KeyChanged = null;
    }

    // ---- user32 / kernel32 ----

    private delegate IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg message, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, StringBuilder text, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
