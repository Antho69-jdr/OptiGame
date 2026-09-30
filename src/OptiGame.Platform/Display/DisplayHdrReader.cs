using System.Runtime.InteropServices;
using OptiGame.Core.Abstractions;

namespace OptiGame.Platform.Display;

/// <summary>
/// État HDR des écrans actifs : QueryDisplayConfig puis DisplayConfigGetDeviceInfo (GET_TARGET_NAME et
/// GET_ADVANCED_COLOR_INFO). Vérifié sur la machine de dev le 2026-09-30 : Samsung LC34G55T « pris en charge + activé »
/// (value = 0x3), BenQ GW2470 sans HDR (0x5 : couleur étendue SDR). Lecture seule, sans droits administrateur.
/// </summary>
public sealed class DisplayHdrReader : IDisplayHdrInfo
{
    private const uint QdcOnlyActivePaths = 2;
    private const uint GetTargetName = 2;
    private const uint GetAdvancedColorInfo = 9;

    public IReadOnlyList<DisplayHdrInfo> GetDisplays()
    {
        if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0) return [];
        var paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return [];

        var displays = new List<DisplayHdrInfo>();
        for (var i = 0; i < pathCount; i++)
        {
            var target = paths[i].target;
            var name = new TargetName { header = Header(GetTargetName, Marshal.SizeOf<TargetName>(), target) };
            var color = new AdvancedColor { header = Header(GetAdvancedColorInfo, Marshal.SizeOf<AdvancedColor>(), target) };
            if (DisplayConfigGetDeviceInfo(ref color) != 0) continue;
            var friendly = DisplayConfigGetDeviceInfo(ref name) == 0 && !string.IsNullOrWhiteSpace(name.monitorFriendlyDeviceName)
                ? name.monitorFriendlyDeviceName
                : $"Écran {i + 1}";
            displays.Add(DisplayHdrInfo.FromAdvancedColor(friendly, color.value)); // interprétation des bits : Core, testée
        }
        return displays;
    }

    private static DeviceInfoHeader Header(uint type, int size, PathTarget target) =>
        new() { type = type, size = (uint)size, adapterId = target.adapterId, id = target.id };

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

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColor { public DeviceInfoHeader header; public uint value; public uint colorEncoding; public uint bitsPerColorChannel; }

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

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColor info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref TargetName info);
}
