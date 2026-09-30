using System.Text;
using Microsoft.Extensions.DependencyInjection;
using OptiGame.Core;
using OptiGame.Core.Diagnostics;
using OptiGame.Core.Library;
using OptiGame.Core.Measurement;
using OptiGame.Core.State;
using OptiGame.Platform;

Console.OutputEncoding = Encoding.UTF8;

// Chemins isolés : l'outil ne touche jamais aux journaux de l'appli.
var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "OptiGame.DiagDump"));
using var services = new ServiceCollection().AddOptiGamePlatform(paths).BuildServiceProvider();

// --import-capture <csv> <libellé> <date ISO> : ajoute un CSV PresentMon aux captures (tests de l'UI avec des
// données fictives). Refusé sans OPTIGAME_DATA_DIR, pour ne jamais écrire dans les vraies données.
if (args.Length == 4 && args[0] == "--import-capture")
{
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPTIGAME_DATA_DIR")))
    {
        Console.Error.WriteLine("OPTIGAME_DATA_DIR doit être défini.");
        return;
    }
    var store = new CaptureStore(new JsonStateStore<CapturesDocument>(Path.Combine(AppPaths.Default.CapturesDir, "captures.json")),
        AppPaths.Default.CapturesDir);
    using var reader = new StreamReader(args[1]);
    var frames = PresentMonCsv.MainSwapChain(PresentMonCsv.Parse(reader));
    store.Add(new CaptureRecord
    {
        Label = args[2],
        ProcessName = frames[0].Application,
        CapturedAt = DateTimeOffset.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture),
        CsvFile = Path.GetRelativePath(AppPaths.Default.CapturesDir, args[1]),
        Stats = FrameStats.Compute(frames.Select(f => f.MsBetweenPresents).ToList()),
    });
    Console.WriteLine($"Importé : {args[2]} ({frames.Count} images)");
    return;
}

// --launch-plan : ce que « Jouer » lancerait pour chaque profil (lecture seule des vrais profils, rien n'est lancé).
if (args.Length == 1 && args[0] == "--launch-plan")
{
    var profiles = new OptiGame.Core.Profiles.ProfileStore(
        new JsonStateStore<OptiGame.Core.Profiles.ProfilesDocument>(AppPaths.Default.Profiles));
    var launcher = services.GetRequiredService<OptiGame.Platform.Processes.GameLauncher>();
    foreach (var profile in profiles.GetAll())
    {
        try
        {
            var plan = launcher.Plan(profile);
            Console.WriteLine($"{profile.Name} [{profile.LaunchMode}] → {plan.Description}\n      {plan.CommandLine}");
        }
        catch (OptiGame.Core.Launching.LaunchException ex)
        {
            Console.WriteLine($"{profile.Name} [{profile.LaunchMode}] → ERREUR : {ex.Message}");
        }
    }
    return;
}

// --graphics : état HDR des écrans et préférences graphiques par jeu (lecture seule).
if (args.Length == 1 && args[0] == "--graphics")
{
    foreach (var display in services.GetRequiredService<OptiGame.Core.Abstractions.IDisplayHdrInfo>().GetDisplays())
        Console.WriteLine($"Écran « {display.Name} » : HDR pris en charge = {display.HdrSupported}, activé = {display.HdrEnabled}");
    var profiles = new OptiGame.Core.Profiles.ProfileStore(new JsonStateStore<OptiGame.Core.Profiles.ProfilesDocument>(AppPaths.Default.Profiles));
    var registry = services.GetRequiredService<OptiGame.Core.Abstractions.IRegistryReader>();
    var settings = services.GetRequiredService<SettingAccessors>();
    var names = registry.GetValueNames(OptiGame.Core.Settings.KnownSettings.GpuPreferencesKey);
    foreach (var profile in profiles.GetAll())
    {
        Console.WriteLine(profile.Name);
        foreach (var name in OptiGame.Core.Settings.GameGraphics.TargetNames(profile.ExePath, names))
        {
            var value = settings.Read(OptiGame.Core.Settings.KnownSettings.GpuPreference(name));
            Console.WriteLine($"      {name} = {(value.IsAbsent ? "(absent)" : value.Text)} → {OptiGame.Core.Settings.GameGraphics.Read(value.Text)}");
        }
    }
    return;
}

// --drivers : pilote graphique (service de NVIDIA) et pilotes proposés par Windows Update. Lecture seule : rien n'est installé.
if (args.Length == 1 && args[0] == "--drivers")
{
    string? nvidiaLatest = null;
    foreach (var gpu in services.GetRequiredService<OptiGame.Core.Abstractions.IGpuInfoProvider>().GetAdapters().Where(g => g.IsPhysical))
    {
        var status = await services.GetRequiredService<OptiGame.Platform.Drivers.NvidiaDriverClient>().CheckAsync(gpu);
        nvidiaLatest ??= status.Latest?.Version;
        Console.WriteLine($"{status.GpuName} [{status.Vendor}] : {status.State} — {status.Message}");
        if (status.Latest is { } l) Console.WriteLine($"      {l.Name} {l.Version} du {l.ReleaseDate:dd/MM/yyyy}, {l.SizeText}\n      {l.DownloadUrl}");
    }
    Console.WriteLine("Recherche Windows Update (≈ 30 s)…");
    foreach (var update in services.GetRequiredService<OptiGame.Platform.Drivers.WindowsUpdateDriverSearch>().Search())
    {
        var hidden = OptiGame.Core.Drivers.DriverRules.IsSupersededByNvidia(update, nvidiaLatest) ? "  [masqué : NVIDIA propose plus récent]" : "";
        Console.WriteLine($"  {update.Title} | {update.DriverClass} | {update.DriverDate:dd/MM/yyyy} | redémarrage possible : {update.MayRequireReboot} | {update.SizeBytes / 1048576.0:N1} Mo{hidden}");
    }
    return;
}

// --steam-playtime : temps de jeu Steam de chaque profil (lecture seule de localconfig.vdf et des vrais profils).
if (args.Length == 1 && args[0] == "--steam-playtime")
{
    var profiles = new OptiGame.Core.Profiles.ProfileStore(
        new JsonStateStore<OptiGame.Core.Profiles.ProfilesDocument>(AppPaths.Default.Profiles));
    using var reader = services.GetRequiredService<OptiGame.Platform.Library.SteamPlaytimeReader>();
    Console.WriteLine($"Fichier lu : {OptiGame.Platform.Library.SteamPlaytimeReader.LocalConfigPath() ?? "(aucun)"}");
    var steam = reader.Read(profiles.GetAll());
    foreach (var profile in profiles.GetAll())
    {
        Console.WriteLine(steam.TryGetValue(profile.Id, out var entry)
            ? $"{profile.Name} : {OptiGame.Core.Playtime.PlaytimeText.Duration(entry.Total)} selon Steam, dernière partie {entry.LastPlayed?.ToLocalTime():dd/MM/yyyy HH:mm}"
            : $"{profile.Name} : aucun temps Steam (jeu hors Steam ou jamais lancé par Steam)");
    }
    return;
}

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
