using Microsoft.Win32;
using OptiGame.Core.State;

namespace OptiGame.Platform.Registry;

/// <summary>Lecture/écriture brute du registre (vue 64 bits), avec conversion vers <see cref="SettingValue"/>.</summary>
internal static class RegistryIO
{
    public static (RegistryHive Hive, string SubKey) Split(string keyPath)
    {
        var slash = keyPath.IndexOf('\\');
        var root = slash < 0 ? keyPath : keyPath[..slash];
        var sub = slash < 0 ? "" : keyPath[(slash + 1)..];
        var hive = root.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            _ => throw new NotSupportedException($"Ruche de registre non prise en charge : {root}"),
        };
        return (hive, sub);
    }

    public static SettingValue Read(string keyPath, string? name)
    {
        var (hive, sub) = Split(keyPath);
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(sub, writable: false);
        if (key is null)
        {
            return SettingValue.Absent;
        }

        var raw = key.GetValue(name ?? "", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null)
        {
            return SettingValue.Absent;
        }

        return key.GetValueKind(name ?? "") switch
        {
            RegistryValueKind.DWord => SettingValue.DWord(unchecked((uint)(int)raw)),
            RegistryValueKind.QWord => SettingValue.QWord(unchecked((ulong)(long)raw)),
            RegistryValueKind.String => SettingValue.String((string)raw),
            RegistryValueKind.ExpandString => SettingValue.ExpandString((string)raw),
            RegistryValueKind.MultiString => SettingValue.MultiString((string[])raw),
            RegistryValueKind.Binary => SettingValue.Binary((byte[])raw),
            var other => throw new NotSupportedException($"Type de valeur de registre non pris en charge : {other}"),
        };
    }

    public static void Write(string keyPath, string? name, SettingValue value)
    {
        var (hive, sub) = Split(keyPath);
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

        if (value.IsAbsent)
        {
            // La clé éventuellement créée par l'appli reste (vide) : sans effet sur Windows.
            using var existing = baseKey.OpenSubKey(sub, writable: true);
            existing?.DeleteValue(name ?? "", throwOnMissingValue: false);
            return;
        }

        using var key = baseKey.CreateSubKey(sub, writable: true);
        switch (value.Kind)
        {
            case SettingValueKind.DWord:
                key.SetValue(name ?? "", unchecked((int)(uint)value.Number!.Value), RegistryValueKind.DWord);
                break;
            case SettingValueKind.QWord:
                key.SetValue(name ?? "", value.Number!.Value, RegistryValueKind.QWord);
                break;
            case SettingValueKind.String:
                key.SetValue(name ?? "", value.Text!, RegistryValueKind.String);
                break;
            case SettingValueKind.ExpandString:
                key.SetValue(name ?? "", value.Text!, RegistryValueKind.ExpandString);
                break;
            case SettingValueKind.MultiString:
                key.SetValue(name ?? "", value.Lines!, RegistryValueKind.MultiString);
                break;
            case SettingValueKind.Binary:
                key.SetValue(name ?? "", value.Bytes!, RegistryValueKind.Binary);
                break;
            default:
                throw new NotSupportedException($"Type de valeur non pris en charge : {value.Kind}");
        }
    }

    public static IReadOnlyList<string> GetValueNames(string keyPath)
    {
        var (hive, sub) = Split(keyPath);
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(sub, writable: false);
        return key?.GetValueNames() ?? [];
    }
}
