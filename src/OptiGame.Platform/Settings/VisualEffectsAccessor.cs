using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Platform.Settings;

/// <summary>
/// Effets visuels de Windows (réglage de partie « Couper les effets visuels ») : animations par SystemParametersInfo (API
/// documentée, par utilisateur, sans droits administrateur), transparence par sa valeur de registre puis la diffusion de
/// WM_SETTINGCHANGE « ImmersiveColorSet » (ce que fait l'application Paramètres). Valeurs DWord 1/0 ; transparence absente =
/// valeur par défaut de Windows, restaurée par suppression.
/// </summary>
public sealed class VisualEffectsAccessor : ISettingAccessor
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string TransparencyValue = "EnableTransparency";

    private const uint SpiGetAnimation = 0x0048;
    private const uint SpiSetAnimation = 0x0049;
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const uint SpiSetClientAreaAnimation = 0x1043;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    public string Kind => KnownSettings.VisualEffectKind;

    public SettingValue Read(SettingTarget target)
    {
        switch (target.Path)
        {
            case "client-area-animation":
                var enabled = false;
                if (!SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref enabled, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return SettingValue.DWord(enabled ? 1u : 0u);
            case "window-animation":
                var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>() };
                if (!SystemParametersInfo(SpiGetAnimation, info.Size, ref info, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return SettingValue.DWord(info.MinAnimate != 0 ? 1u : 0u);
            case "transparency":
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    return key?.GetValue(TransparencyValue) is int value ? SettingValue.DWord(unchecked((uint)value)) : SettingValue.Absent;
                }
            default:
                throw new ArgumentException($"Effet visuel inconnu : {target.Path}");
        }
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        var on = value.AsDWord() is { } number ? number != 0 : value.IsAbsent ? (bool?)null : throw new ArgumentException($"Valeur invalide : {value}");
        switch (target.Path)
        {
            case "client-area-animation":
                // pvParam = la valeur BOOL elle-même (documentation de SPI_SETCLIENTAREAANIMATION).
                if (!SystemParametersInfo(SpiSetClientAreaAnimation, 0, (IntPtr)(on == false ? 0 : 1), SpifUpdateIniFile | SpifSendChange))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                break;
            case "window-animation":
                var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>(), MinAnimate = on == false ? 0 : 1 };
                if (!SystemParametersInfo(SpiSetAnimation, info.Size, ref info, SpifUpdateIniFile | SpifSendChange)) throw new Win32Exception(Marshal.GetLastWin32Error());
                break;
            case "transparency":
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(PersonalizeKey))
                {
                    if (on is { } enabled) key.SetValue(TransparencyValue, enabled ? 1 : 0, RegistryValueKind.DWord);
                    else key.DeleteValue(TransparencyValue, throwOnMissingValue: false);
                }
                // L'Explorateur et les applications relisent la transparence à la réception de ce message.
                SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "ImmersiveColorSet", 0x0002, 2000, out _);
                break;
            default:
                throw new ArgumentException($"Effet visuel inconnu : {target.Path}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo
    {
        public uint Size;
        public int MinAnimate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref AnimationInfo value, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
