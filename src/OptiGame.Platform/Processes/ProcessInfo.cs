using System.Diagnostics;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Profiles;
using static OptiGame.Platform.Native.NativeMethods;

namespace OptiGame.Platform.Processes;

internal static class ProcessInfo
{
    private static readonly string WindowsDir =
        Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) + Path.DirectorySeparatorChar;

    public static int CurrentSessionId { get; } = Process.GetCurrentProcess().SessionId;

    public static int CurrentProcessId { get; } = Environment.ProcessId;

    /// <summary>Chemin complet de l'exe (fonctionne aussi pour les processus élevés ou protégés).</summary>
    public static string? GetPath(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Exécutable situé dans le dossier Windows : jamais fermé par OptiGame.</summary>
    public static bool IsWindowsComponent(string? path) =>
        path is not null && path.StartsWith(WindowsDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>Instances d'un exe dans la session de l'utilisateur (hors OptiGame).</summary>
    public static List<Process> InSession(string exeName)
    {
        var baseName = Path.GetFileNameWithoutExtension(exeName);
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName(baseName))
        {
            if (process.SessionId == CurrentSessionId && process.Id != CurrentProcessId) result.Add(process);
            else process.Dispose();
        }
        return result;
    }
}

public sealed class RunningProgramsProvider : IRunningProgramsProvider
{
    public IReadOnlyList<RunningProgram> GetUserPrograms()
    {
        var entries = new List<(string ExeName, string Path)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.SessionId != ProcessInfo.CurrentSessionId || process.Id == ProcessInfo.CurrentProcessId) continue;
                var path = ProcessInfo.GetPath(process.Id);
                if (path is null || ProcessInfo.IsWindowsComponent(path)) continue;
                var exeName = Path.GetFileName(path);
                if (ProfileValidator.IsProtected(exeName)) continue;
                entries.Add((exeName, path));
            }
        }

        return entries
            .GroupBy(e => e.ExeName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RunningProgram(g.Key, Describe(g.First().Path), g.First().Path, g.Count()))
            .OrderBy(p => p.ExeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? Describe(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
