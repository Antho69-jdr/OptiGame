using System.Runtime.InteropServices;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Display;
using static OptiGame.Platform.Native.NativeMethods;

namespace OptiGame.Platform.Display;

/// <summary>
/// Liaison des écrans actifs, lecture seule et sans droits administrateur : QueryDisplayConfig puis DisplayConfigGetDeviceInfo
/// (GET_SOURCE_NAME = « \\.\DISPLAY6 », GET_TARGET_NAME = nom, chemin du moniteur et type de connexion), EDID lu dans
/// HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\&lt;modèle&gt;\&lt;instance&gt;\Device Parameters (instance tirée du chemin
/// « \\?\DISPLAY#SAM711A#5&amp;80baa16&amp;0&amp;UID155907#{…} »), carte d'affichage et modes de Windows par EnumDisplayDevices /
/// EnumDisplaySettings. Vérifié le 2026-10-07 sur la machine de dev (Samsung LC34G55T, BenQ GW2470 en portrait).
/// </summary>
public sealed class DisplayLinkReader : IDisplayLinkInfo
{
    private const uint QdcOnlyActivePaths = 2;
    private const uint GetSourceName = 1;
    private const uint GetTargetName = 2;

    public IReadOnlyList<DisplayLink> GetLinks()
    {
        if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0) return [];
        var paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return [];

        var adapters = Adapters();
        var links = new List<DisplayLink>();
        for (var i = 0; i < pathCount; i++)
        {
            var source = new SourceName { header = Header(GetSourceName, Marshal.SizeOf<SourceName>(), paths[i].source.adapterId, paths[i].source.id) };
            var target = new TargetName { header = Header(GetTargetName, Marshal.SizeOf<TargetName>(), paths[i].target.adapterId, paths[i].target.id) };
            if (DisplayConfigGetDeviceInfo(ref source) != 0 || DisplayConfigGetDeviceInfo(ref target) != 0) continue;

            var gdiName = source.viewGdiDeviceName;
            var edid = ReadEdid(target.monitorDevicePath);
            int? windowsMax = null;
            if (edid is not null && Edid.Native(Edid.Timings(edid)) is { } native)
            {
                // Les modes sont énumérés dans l'orientation actuelle (écran en portrait : 1080×1920 pour un écran 1920×1080).
                windowsMax = DisplayService.EnumerateModes(gdiName)
                    .Where(m => (m.dmPelsWidth == native.Width && m.dmPelsHeight == native.Height) ||
                                (m.dmPelsWidth == native.Height && m.dmPelsHeight == native.Width))
                    .Select(m => (int?)m.dmDisplayFrequency)
                    .Max();
            }
            var adapter = adapters.GetValueOrDefault(gdiName);
            links.Add(new DisplayLink(gdiName, target.monitorFriendlyDeviceName, adapter.Gpu ?? "", Connection(target.outputTechnology), edid,
                windowsMax, adapter.IsPrimary));
        }
        return links;
    }

    /// <summary>Carte d'affichage de chaque sortie de Windows (« \\.\DISPLAY6 » → « NVIDIA GeForce RTX 3070 », principale ou non).</summary>
    private static Dictionary<string, (string? Gpu, bool IsPrimary)> Adapters()
    {
        var result = new Dictionary<string, (string?, bool)>(StringComparer.OrdinalIgnoreCase);
        for (uint i = 0; ; i++)
        {
            var adapter = DisplayDevice.Create();
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            result[adapter.DeviceName] = (adapter.DeviceString, (adapter.StateFlags & DisplayDevicePrimaryDevice) != 0);
        }
        return result;
    }

    private static byte[]? ReadEdid(string? monitorDevicePath)
    {
        var parts = monitorDevicePath?.Split('#');
        if (parts is not { Length: >= 3 }) return null;
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
        return key?.GetValue("EDID") as byte[];
    }

    /// <summary>DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY (wingdi.h).</summary>
    private static DisplayConnection Connection(uint technology) => technology switch
    {
        0 => DisplayConnection.Vga,
        4 => DisplayConnection.Dvi,
        5 => DisplayConnection.Hdmi,
        10 => DisplayConnection.DisplayPort,
        18 => DisplayConnection.UsbC,                  // DISPLAYPORT_USB_TUNNEL
        6 or 11 or 13 or 0x80000000 => DisplayConnection.Internal, // LVDS, DisplayPort / UDI intégrés, INTERNAL
        15 => DisplayConnection.Wireless,              // MIRACAST
        _ => DisplayConnection.Other,
    };

    private static DeviceInfoHeader Header(uint type, int size, Luid adapter, uint id) => new() { type = type, size = (uint)size, adapterId = adapter, id = id };

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)] private struct PathSource { public Luid adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathTarget
    {
        public Luid adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public Rational refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)] private struct PathInfo { public PathSource source; public PathTarget target; public uint flags; }

    [StructLayout(LayoutKind.Sequential, Size = 64)] private struct ModeInfo { public uint infoType; }

    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfoHeader { public uint type; public uint size; public Luid adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public DeviceInfoHeader header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetName
    {
        public DeviceInfoHeader header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] ModeInfo[] modes, IntPtr topology);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceName info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref TargetName info);
}
