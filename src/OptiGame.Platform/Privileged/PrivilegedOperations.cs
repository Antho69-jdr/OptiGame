using Microsoft.Win32;
using OptiGame.Core.State;
using OptiGame.Platform.Registry;

namespace OptiGame.Platform.Privileged;

/// <summary>
/// Point de passage unique de toutes les opérations qui exigent les droits administrateur.
/// En v1, l'appli entière est élevée (manifeste requireAdministrator) et cette interface est implémentée
/// dans le processus. Pour passer en asInvoker, il suffira de l'implémenter via un processus/service élevé.
/// </summary>
public interface IPrivilegedOperations
{
    /// <summary>Écrit une valeur sous HKEY_LOCAL_MACHINE.</summary>
    void WriteMachineRegistryValue(string keyPath, string? name, SettingValue value);
}

public sealed class InProcessPrivilegedOperations : IPrivilegedOperations
{
    public void WriteMachineRegistryValue(string keyPath, string? name, SettingValue value)
    {
        if (RegistryIO.Split(keyPath).Hive != RegistryHive.LocalMachine)
        {
            throw new ArgumentException("Seules les clés HKLM passent par les opérations privilégiées.", nameof(keyPath));
        }

        RegistryIO.Write(keyPath, name, value);
    }
}
