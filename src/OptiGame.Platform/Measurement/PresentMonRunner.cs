using System.Diagnostics;
using OptiGame.Core;
using OptiGame.Core.Logging;
using OptiGame.Core.Measurement;

namespace OptiGame.Platform.Measurement;

/// <summary>
/// Lance PresentMon (console) pour une capture. PresentMon utilise ETW et nécessite les droits administrateur :
/// il hérite de ceux d'OptiGame. Aucun processus ne reste en arrière-plan après la capture.
/// </summary>
public sealed class PresentMonRunner(AppPaths paths, FileLog log)
{
    /// <summary>Dossier où OptiGame range les outils téléchargés (%LocalAppData%\OptiGame\tools).</summary>
    public string ToolsDirectory => Path.Combine(paths.Root, "tools");

    /// <summary>Outils fournis avec l'appli par l'installeur (PresentMon dans Program Files\OptiGame\tools).</summary>
    public static string BundledToolsDirectory => Path.Combine(AppContext.BaseDirectory, "tools");

    /// <summary>
    /// PresentMon le plus récent du dossier des outils de l'utilisateur, sinon celui fourni par l'installeur, ou null : une
    /// version déposée par l'utilisateur passe avant celle de l'installeur.
    /// </summary>
    public string? FindInToolsDirectory() => Newest(ToolsDirectory) ?? Newest(BundledToolsDirectory);

    private static string? Newest(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "PresentMon*.exe").OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            : null;

    public async Task<string> RunAsync(CaptureRequest request, CancellationToken cancellation)
    {
        if (!File.Exists(request.PresentMonPath))
        {
            throw new FileNotFoundException($"PresentMon introuvable : {request.PresentMonPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputCsv)!);
        var start = new ProcessStartInfo(request.PresentMonPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in request.BuildArguments())
        {
            start.ArgumentList.Add(argument);
        }

        log.Info($"Capture PresentMon : {request.ProcessName}, {request.DurationSeconds} s (délai {request.DelaySeconds} s) → {request.OutputCsv}");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PresentMon n'a pas pu être lancé.");
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            await StopSessionAsync(request.PresentMonPath, request.Session);
            log.Info("Capture annulée par l'utilisateur.");
            throw;
        }

        var output = ((await stdout) + (await stderr)).Trim();
        log.Info($"PresentMon terminé (code {process.ExitCode}). {output}");
        return output;
    }

    /// <summary>Ferme la session ETW si PresentMon a été arrêté de force.</summary>
    private static async Task StopSessionAsync(string presentMonPath, string session)
    {
        var start = new ProcessStartInfo(presentMonPath) { CreateNoWindow = true, UseShellExecute = false };
        start.ArgumentList.Add("--session_name");
        start.ArgumentList.Add(session);
        start.ArgumentList.Add("--terminate_existing_session");
        using var stop = Process.Start(start);
        if (stop is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await stop.WaitForExitAsync(timeout.Token);
        }
    }
}
