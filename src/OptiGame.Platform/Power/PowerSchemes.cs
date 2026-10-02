using System.ComponentModel;
using System.Runtime.InteropServices;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Settings;
using OptiGame.Core.State;
using static OptiGame.Platform.Native.NativeMethods;

namespace OptiGame.Platform.Power;

/// <summary>
/// Plans d'alimentation via powrprof.dll (la sortie de powercfg est localisée, on ne la parse pas).
/// Changer de plan ne nécessite pas les droits administrateur.
/// </summary>
public sealed class PowerSchemeService : IPowerSchemeProvider, ISettingAccessor
{
    public string Kind => KnownSettings.PowerSchemeKind;

    public Guid GetActiveScheme()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var ptr), nameof(PowerGetActiveScheme));
        try
        {
            return Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            LocalFree(ptr);
        }
    }

    public IReadOnlyList<PowerScheme> GetSchemes()
    {
        var schemes = new List<PowerScheme>();
        for (uint index = 0; ; index++)
        {
            var guid = Guid.Empty;
            var size = (uint)Marshal.SizeOf<Guid>();
            var result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, ref guid, ref size);
            if (result == ErrorNoMoreItems) break;
            Check(result, nameof(PowerEnumerate));
            schemes.Add(new PowerScheme(guid, ReadFriendlyName(guid)));
        }
        return schemes;
    }

    public uint? ReadAcValue(Guid scheme, Guid subgroup, Guid setting)
    {
        var result = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var value);
        if (result == ErrorFileNotFound) return null;
        Check(result, nameof(PowerReadACValueIndex));
        return value;
    }

    public SettingValue Read(SettingTarget target) => SettingValue.String(GetActiveScheme().ToString());

    public void Write(SettingTarget target, SettingValue value)
    {
        if (value.Text is null || !Guid.TryParse(value.Text, out var guid))
        {
            throw new ArgumentException($"GUID de plan d'alimentation invalide : {value}");
        }
        Check(PowerSetActiveScheme(IntPtr.Zero, ref guid), nameof(PowerSetActiveScheme));
    }

    private static string ReadFriendlyName(Guid scheme)
    {
        uint size = 0;
        var result = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if ((result != 0 && result != ErrorMoreData) || size == 0)
        {
            return scheme.ToString();
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            result = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
            return result == 0 ? Marshal.PtrToStringUni(buffer) ?? scheme.ToString() : scheme.ToString();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void Check(uint result, string function)
    {
        if (result != 0) throw new Win32Exception((int)result, $"{function} a échoué ({result}).");
    }
}

public sealed class PowerStatusProvider : IPowerStatusProvider
{
    private const byte NoSystemBattery = 128;
    private const byte UnknownStatus = 255;

    public PowerStatus GetStatus()
    {
        if (!GetSystemPowerStatus(out var s))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var hasBattery = s.BatteryFlag != UnknownStatus && (s.BatteryFlag & NoSystemBattery) == 0;
        bool? onAc = s.ACLineStatus switch { 0 => false, 1 => true, _ => null };
        int? percent = hasBattery && s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : null;
        return new PowerStatus(hasBattery, onAc, percent, EnergySaverOn: s.SystemStatusFlag == 1);
    }
}

/// <summary>
/// Réglages d'un plan d'alimentation, sur secteur (cible <see cref="KnownSettings.PowerSetting"/>). Lecture directe ; écriture
/// par <see cref="Privileged.IPrivilegedOperations"/> (les plans du système appartiennent à l'administrateur).
/// </summary>
public sealed class PowerSettingAccessor(PowerSchemeService schemes, Privileged.IPrivilegedOperations privileged) : ISettingAccessor
{
    public string Kind => KnownSettings.PowerSettingKind;

    public SettingValue Read(SettingTarget target)
    {
        var (scheme, subgroup, setting) = Parse(target);
        return schemes.ReadAcValue(scheme, subgroup, setting) is { } value ? SettingValue.DWord(value) : SettingValue.Absent;
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        var (scheme, subgroup, setting) = Parse(target);
        // Un réglage absent du plan ne peut pas être « supprimé » : OptiGame ne corrige qu'un réglage existant.
        var number = value.AsDWord() ?? throw new ArgumentException($"Valeur de réglage d'alimentation invalide : {value}");
        privileged.WriteAcPowerSetting(scheme, subgroup, setting, number);
    }

    private static (Guid Scheme, Guid Subgroup, Guid Setting) Parse(SettingTarget target)
    {
        var parts = target.Path.Split('/');
        if (parts.Length != 3 || !Guid.TryParse(parts[0], out var scheme) || !Guid.TryParse(parts[1], out var subgroup) ||
            !Guid.TryParse(parts[2], out var setting) || target.Name != "ac")
        {
            throw new ArgumentException($"Réglage d'alimentation invalide : {target.Path} ({target.Name})");
        }
        return (scheme, subgroup, setting);
    }
}
