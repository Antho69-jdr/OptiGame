using System.ComponentModel;
using System.Diagnostics;
using OptiGame.Core.Measurement;
using OptiGame.Platform.Processes;

namespace OptiGame.Platform.Measurement;

/// <summary>
/// Temps processeur des processus de la session de l'utilisateur, à un instant (lecture seule). Un processus illisible (protégé,
/// ou terminé entre-temps) est simplement ignoré.
/// </summary>
public static class ProcessCpuSampler
{
    public static IReadOnlyList<ProcessCpuSample> Snapshot()
    {
        var samples = new List<ProcessCpuSample>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != ProcessInfo.CurrentSessionId || process.Id == ProcessInfo.CurrentProcessId) continue;
                    var path = ProcessInfo.GetPath(process.Id);
                    var exeName = path is not null ? Path.GetFileName(path) : process.ProcessName + ".exe";
                    samples.Add(new ProcessCpuSample(process.Id, exeName, path, process.TotalProcessorTime));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // accès refusé ou processus terminé : ignoré
                }
            }
        }
        return samples;
    }

    /// <summary>Exécutable du dossier Windows (jamais proposé à la fermeture).</summary>
    public static bool IsWindowsComponent(string? path) => ProcessInfo.IsWindowsComponent(path);
}
