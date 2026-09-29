using System.Text;
using Microsoft.Extensions.DependencyInjection;
using OptiGame.Core;
using OptiGame.Core.Diagnostics;
using OptiGame.Core.Library;
using OptiGame.Platform;

Console.OutputEncoding = Encoding.UTF8;

// Chemins isolés : l'outil ne touche jamais aux journaux de l'appli.
var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "OptiGame.DiagDump"));
using var services = new ServiceCollection().AddOptiGamePlatform(paths).BuildServiceProvider();

// dotnet run --project tools/OptiGame.DiagDump -- --games [dossier…] : recherche des jeux installés.
if (args.Length > 0 && args[0] == "--games")
{
    foreach (var game in services.GetRequiredService<IGameLibraryScanner>().Scan(args.Skip(1).ToList()))
    {
        Console.WriteLine($"[{game.Source}] {game.Name} — {game.Folder}");
        foreach (var exe in game.Candidates)
        {
            Console.WriteLine($"      {exe.SizeBytes / (1024.0 * 1024),8:N1} Mo  {exe.Path}");
        }
    }
    return;
}

foreach (var result in services.GetRequiredService<DiagnosticRunner>().RunAll())
{
    var status = result.Status switch
    {
        DiagnosticStatus.Ok => "OK        ",
        DiagnosticStatus.NeedsAttention => "À CORRIGER",
        DiagnosticStatus.Info => "INFO      ",
        _ => "ERREUR    ",
    };
    Console.WriteLine($"[{status}] {result.Title}{(result.IsEstimate ? " (estimation)" : "")}");
    Console.WriteLine($"             {result.Summary}");
    foreach (var detail in result.Details)
    {
        Console.WriteLine($"               · {detail}");
    }
    if (result.Status == DiagnosticStatus.Error)
    {
        Console.WriteLine($"               ! {result.Explanation}");
    }
    foreach (var fix in result.Fixes)
    {
        Console.WriteLine($"               → correction proposée{(fix.IsAdvanced ? " (avancée)" : "")} : {fix.Change.Title}");
    }
    Console.WriteLine();
}
