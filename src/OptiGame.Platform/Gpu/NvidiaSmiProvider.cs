using System.Diagnostics;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Gpu;
using OptiGame.Core.Logging;

namespace OptiGame.Platform.Gpu;

/// <summary>
/// nvidia-smi, installé avec le pilote NVIDIA : %SystemRoot%\System32 (pilotes DCH, machine de dev) ou, sur les anciens pilotes,
/// Program Files\NVIDIA Corporation\NVSMI. Lecture seule, sans droits administrateur, lancé seulement à la demande.
/// </summary>
public sealed class NvidiaSmiProvider(FileLog log) : INvidiaInfoProvider
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static string? FindExe() =>
        new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
        }.FirstOrDefault(File.Exists);

    public IReadOnlyList<NvidiaGpuMemory>? GetMemory()
    {
        var output = Run(NvidiaSmi.QueryArguments);
        if (output is null) return null;
        try
        {
            return NvidiaSmi.ParseMemory(output);
        }
        catch (System.Xml.XmlException ex)
        {
            log.Error("Réponse de nvidia-smi illisible", ex);
            return null;
        }
    }

    /// <summary>
    /// Relevés d'une seconde pendant <paramref name="duration"/> (mesure automatique) ; liste vide si l'outil est absent.
    /// Un seul processus nvidia-smi, arrêté à la fin ou à l'annulation (partie terminée).
    /// </summary>
    public async Task<IReadOnlyList<GpuSample>> SampleAsync(TimeSpan duration, CancellationToken cancellation)
    {
        if (FindExe() is not { } exe) return [];
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in GpuSampling.Arguments(1000)) start.ArgumentList.Add(argument);

        var samples = new List<GpuSample>();
        using var process = Process.Start(start);
        if (process is null) return [];
        _ = process.StandardError.ReadToEndAsync();
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        window.CancelAfter(duration);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(window.Token) is { } line)
            {
                if (GpuSampling.ParseLine(line) is { } sample) samples.Add(sample);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            // Durée écoulée : fin normale des relevés.
        }
        finally
        {
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (InvalidOperationException)
            {
                // déjà terminé
            }
        }
        cancellation.ThrowIfCancellationRequested();
        return samples;
    }

    /// <summary>Sortie standard, ou null si l'outil est absent, échoue ou dépasse 10 s.</summary>
    public string? Run(IReadOnlyList<string> arguments)
    {
        if (FindExe() is not { } exe) return null;
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync(); // vidé pour ne jamais bloquer l'outil
            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                log.Warn("nvidia-smi n'a pas répondu en 10 s.");
                return null;
            }
            if (process.ExitCode != 0)
            {
                log.Warn($"nvidia-smi a échoué (code {process.ExitCode}).");
                return null;
            }
            return output.Result;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            log.Warn($"nvidia-smi impossible à lancer : {ex.Message}");
            return null;
        }
    }
}
