using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Platform.Processes;

/// <summary>
/// Fermeture / relance / priorité des programmes de la session utilisateur.
/// Réglage « process » : valeur = [chemin de l'exe, ligne de commande] s'il tourne, Absent sinon.
/// </summary>
public sealed class ProcessService : IProcessControl, ISettingAccessor
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);

    public string Kind => KnownSettings.ProcessKind;

    public SettingValue Read(SettingTarget target)
    {
        var exeName = target.Path;
        var escaped = exeName.Replace("\\", "\\\\").Replace("'", "\\'");
        var instances = Wmi.Wmi.Query(
            $"SELECT ProcessId, ParentProcessId, SessionId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = '{escaped}'",
            p => new
            {
                Pid = (int)Wmi.Wmi.Get<uint>(p, "ProcessId"),
                ParentPid = (int)Wmi.Wmi.Get<uint>(p, "ParentProcessId"),
                Session = (int)Wmi.Wmi.Get<uint>(p, "SessionId"),
                Path = Wmi.Wmi.Get<string>(p, "ExecutablePath"),
                CommandLine = Wmi.Wmi.Get<string>(p, "CommandLine"),
            })
            .Where(p => p.Session == ProcessInfo.CurrentSessionId && p.Path is not null)
            .ToList();

        if (instances.Count == 0)
        {
            return SettingValue.Absent;
        }

        // Processus racine (ex. le chrome.exe principal, pas ses onglets) : son parent n'est pas une autre instance.
        var pids = instances.Select(i => i.Pid).ToHashSet();
        var root = instances.FirstOrDefault(i => !pids.Contains(i.ParentPid)) ?? instances[0];
        EnsureClosable(exeName, root.Path);
        return SettingValue.MultiString([root.Path!, root.CommandLine ?? $"\"{root.Path}\""]);
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        if (value.IsAbsent)
        {
            Close(target.Path);
            return;
        }

        if (value.Lines is not [var exePath, var commandLine])
        {
            throw new ArgumentException($"Valeur de processus invalide : {value}");
        }

        EnsureClosable(target.Path, exePath);
        var running = ProcessInfo.InSession(target.Path);
        var alreadyRunning = running.Count > 0;
        running.ForEach(p => p.Dispose());
        if (!alreadyRunning)
        {
            UnelevatedLauncher.Launch(exePath, commandLine);
        }
    }

    public int Close(string exeName)
    {
        var processes = ProcessInfo.InSession(exeName);
        try
        {
            foreach (var process in processes)
            {
                EnsureClosable(exeName, ProcessInfo.GetPath(process.Id));
            }

            // 1. Fermeture normale (comme un clic sur la croix) pour les programmes qui ont une fenêtre.
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow();
                }
                catch (InvalidOperationException)
                {
                    // Déjà terminé.
                }
            }

            // 2. Attente, puis arrêt forcé des récalcitrants (programmes de la zone de notification notamment).
            var deadline = Stopwatch.StartNew();
            foreach (var process in processes)
            {
                var remaining = GracePeriod - deadline.Elapsed;
                if (remaining > TimeSpan.Zero) process.WaitForExit(remaining);
            }

            foreach (var process in processes.Where(p => !HasExited(p)))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(TimeSpan.FromSeconds(2));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // Déjà terminé ou accès refusé : signalé par le décompte ci-dessous.
                }
            }

            var stillRunning = processes.Count(p => !HasExited(p));
            if (stillRunning > 0)
            {
                throw new InvalidOperationException($"{stillRunning} instance(s) de {exeName} n'ont pas pu être fermées.");
            }
            return processes.Count;
        }
        finally
        {
            processes.ForEach(p => p.Dispose());
        }
    }

    public string? TrySetPriority(int processId, GamePriority priority)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.PriorityClass = priority switch
            {
                GamePriority.AboveNormal => ProcessPriorityClass.AboveNormal,
                GamePriority.High => ProcessPriorityClass.High,
                _ => ProcessPriorityClass.Normal,
            };
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            return ex.Message;
        }
    }

    public IReadOnlyList<int> FindProcesses(string exePath)
    {
        var result = new List<int>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)))
        {
            using (process)
            {
                if (ExePaths.AreSame(ProcessInfo.GetPath(process.Id), exePath)) result.Add(process.Id);
            }
        }
        return result;
    }

    /// <summary>Double sécurité, en plus de la validation des profils.</summary>
    private static void EnsureClosable(string exeName, string? path)
    {
        if (ProfileValidator.IsProtected(exeName) || ProcessInfo.IsWindowsComponent(path))
        {
            throw new InvalidOperationException($"{exeName} est un composant de Windows : OptiGame ne le ferme pas.");
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
