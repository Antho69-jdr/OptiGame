using OptiGame.Core.Abstractions;
using OptiGame.Core.Diagnostics.Checks;
using OptiGame.Core.Settings;
using OptiGame.Core.State;
using static OptiGame.Platform.Native.NativeMethods;

namespace OptiGame.Platform.Display;

/// <summary>
/// Écrans et modes d'affichage via EnumDisplayDevices / EnumDisplaySettings. Seuls les modes que le moniteur
/// annonce sont énumérés (pas de mode « brut »), et les modes entrelacés sont ignorés.
/// </summary>
public sealed class DisplayService : IDisplayInfoProvider, ISettingAccessor
{
    public string Kind => KnownSettings.DisplayModeKind;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var result = new List<DisplayInfo>();
        for (uint i = 0; ; i++)
        {
            var adapter = DisplayDevice.Create();
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if ((adapter.StateFlags & DisplayDeviceAttachedToDesktop) == 0) continue;

            var current = DevMode.Create();
            if (!EnumDisplaySettings(adapter.DeviceName, EnumCurrentSettings, ref current)) continue;

            // Les modes sont énumérés dans l'orientation courante (vérifié sur un écran en portrait).
            var atCurrentRes = EnumerateModes(adapter.DeviceName)
                .Where(m => m.dmPelsWidth == current.dmPelsWidth && m.dmPelsHeight == current.dmPelsHeight)
                .Select(m => m.dmDisplayFrequency)
                .DefaultIfEmpty(current.dmDisplayFrequency)
                .Max();

            var monitor = DisplayDevice.Create();
            var monitorName = EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0) ? monitor.DeviceString : "";

            result.Add(new DisplayInfo(
                adapter.DeviceName,
                monitorName,
                current.dmPelsWidth,
                current.dmPelsHeight,
                current.dmDisplayFrequency,
                Math.Max(atCurrentRes, current.dmDisplayFrequency),
                (adapter.StateFlags & DisplayDevicePrimaryDevice) != 0));
        }
        return result;
    }

    public SettingValue Read(SettingTarget target)
    {
        var current = DevMode.Create();
        if (!EnumDisplaySettings(target.Path, EnumCurrentSettings, ref current))
        {
            return SettingValue.Absent;
        }
        return SettingValue.String(DisplayModeText.Format(current.dmPelsWidth, current.dmPelsHeight, current.dmDisplayFrequency));
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        if (!DisplayModeText.TryParse(value.Text, out var width, out var height, out var hz))
        {
            throw new ArgumentException($"Mode d'affichage invalide : {value}");
        }

        var mode = EnumerateModes(target.Path)
            .Where(m => m.dmPelsWidth == width && m.dmPelsHeight == height && m.dmDisplayFrequency == hz)
            .OrderByDescending(m => m.dmBitsPerPel)
            .Cast<DevMode?>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Le mode {width}×{height} à {hz} Hz n'est pas disponible sur {target.Path}.");

        mode.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;
        var test = ChangeDisplaySettingsEx(target.Path, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero);
        if (test != DispChangeSuccessful)
        {
            throw new InvalidOperationException($"Windows refuse le mode {width}×{height} à {hz} Hz (code {test}).");
        }

        var applied = ChangeDisplaySettingsEx(target.Path, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
        if (applied is not (DispChangeSuccessful or DispChangeRestart))
        {
            throw new InvalidOperationException($"Échec du changement de mode d'affichage (code {applied}).");
        }
    }

    internal static IEnumerable<DevMode> EnumerateModes(string deviceName)
    {
        for (var i = 0; ; i++)
        {
            var mode = DevMode.Create();
            if (!EnumDisplaySettings(deviceName, i, ref mode)) yield break;
            if ((mode.dmDisplayFlags & DmInterlaced) != 0 || mode.dmDisplayFrequency <= 1) continue;
            yield return mode;
        }
    }
}
