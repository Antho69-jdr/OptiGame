using Microsoft.Win32;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Settings;
using OptiGame.Core.State;
using OptiGame.Platform.Privileged;

namespace OptiGame.Platform.Registry;

/// <summary>
/// Accès au registre. Les écritures HKLM passent par <see cref="IPrivilegedOperations"/> ; les écritures HKCU
/// restent dans le processus. Limite connue : si l'élévation UAC se fait avec un autre compte que celui de la
/// session, HKCU désigne le profil de ce compte-là.
/// </summary>
public sealed class RegistrySettingAccessor(IPrivilegedOperations privileged) : ISettingAccessor, IRegistryReader
{
    public string Kind => KnownSettings.RegistryKind;

    public SettingValue Read(SettingTarget target) => RegistryIO.Read(target.Path, target.Name);

    public void Write(SettingTarget target, SettingValue value)
    {
        if (RegistryIO.Split(target.Path).Hive == RegistryHive.LocalMachine)
        {
            privileged.WriteMachineRegistryValue(target.Path, target.Name, value);
        }
        else
        {
            RegistryIO.Write(target.Path, target.Name, value);
        }
    }

    public IReadOnlyList<string> GetValueNames(string keyPath) => RegistryIO.GetValueNames(keyPath);
}
