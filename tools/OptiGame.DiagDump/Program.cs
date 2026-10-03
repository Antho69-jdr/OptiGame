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
    if (await services.GetRequiredService<OptiGame.Platform.Drivers.AmdChipsetClient>().CheckAsync() is { } chipset)
    {
        Console.WriteLine($"Chipset : {chipset.Description}\n      {chipset.InstalledText}\n      {chipset.State} — {chipset.Message}");
        if (chipset.Latest is { } l) Console.WriteLine($"      {l.Version} du {l.ReleaseDate:dd/MM/yyyy}, {l.SizeText}\n      {l.DownloadUrl}\n      notes : {l.ReleaseNotes}");
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

// --nvidia-profiles : profil NVIDIA appliqué à chaque jeu et son plafond de FPS (lecture seule).
if (args.Length == 1 && args[0] == "--nvidia-profiles")
{
    Console.WriteLine($"Pilote NVIDIA (nvapi64.dll) : {(OptiGame.Platform.Gpu.NvidiaProfiles.IsAvailable ? "présent" : "absent")}");
    var profiles = new OptiGame.Core.Profiles.ProfileStore(
        new JsonStateStore<OptiGame.Core.Profiles.ProfilesDocument>(AppPaths.Default.Profiles));
    foreach (var profile in profiles.GetAll())
    {
        try
        {
            var s = OptiGame.Platform.Gpu.NvidiaProfiles.Read(profile.ExePath, 0x10835002);
            Console.WriteLine($"{profile.Name} : profil {(s.ProfileName is null ? "(aucun : profil global)" : $"« {s.ProfileName} »{(s.ProfileIsPredefined ? " (NVIDIA)" : "")}")}, " +
                              $"plafond propre au profil = {s.Value?.ToString() ?? "non défini"}, appliqué = {s.EffectiveValue?.ToString() ?? "?"}");
        }
        catch (OptiGame.Platform.Gpu.NvidiaApiException ex)
        {
            Console.WriteLine($"{profile.Name} : {ex.Message}");
        }
    }
    return;
}

// --nvidia-selftest <chemin d'un exe FACTICE> : écrit puis retire un plafond de FPS sur ce seul exe (profil « OptiGame - … »
// créé puis supprimé). Ne jamais lui donner un vrai jeu. Écriture : droits administrateur probablement nécessaires.
if (args.Length == 2 && args[0] == "--nvidia-selftest")
{
    var exe = args[1];
    if (File.Exists(exe)) { Console.WriteLine("Refusé : donnez le chemin d'un exe qui N'EXISTE PAS."); return; }
    string Show() { var s = OptiGame.Platform.Gpu.NvidiaProfiles.Read(exe, 0x10835002); return $"profil={s.ProfileName ?? "(aucun)"} propre={s.Value?.ToString() ?? "-"} appliqué={s.EffectiveValue?.ToString() ?? "défaut"}"; }
    try
    {
        // Chaîne complète de l'appli : journal (fichier temporaire) → accesseur → pilote, puis annulation.
        var journalFile = Path.Combine(Path.GetTempPath(), $"optigame-nvidia-selftest-{Guid.NewGuid():N}.json");
        var journal = new ChangeJournal(new JsonStateStore<JournalDocument>(journalFile),
            new SettingAccessors([new OptiGame.Platform.Gpu.NvidiaProfileSettingAccessor()]));
        var id = Guid.NewGuid();
        Console.WriteLine($"avant      : {Show()}");
        journal.Apply(OptiGame.Core.Gpu.FrameRateCap.Change(id, "test", exe, null, null, 100));
        Console.WriteLine($"appliqué   : {Show()}");
        journal.Apply(OptiGame.Core.Gpu.FrameRateCap.Change(id, "test", exe, "OptiGame - test", 100, 60));
        Console.WriteLine($"réappliqué : {Show()}");
        var report = journal.Undo(OptiGame.Core.Gpu.FrameRateCap.ChangeId(id));
        Console.WriteLine($"annulé     : {Show()} (succès : {report.Success})");
        File.Delete(journalFile);
    }
    catch (OptiGame.Platform.Gpu.NvidiaApiException ex)
    {
        Console.WriteLine($"Échec : {ex.Message}");
    }
    return;
}

// --gpu-sample [secondes] : relevés nvidia-smi comme pendant la mesure automatique (lecture seule), puis résumé.
if (args.Length is 1 or 2 && args[0] == "--gpu-sample")
{
    var seconds = args.Length == 2 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 10;
    var smi = services.GetRequiredService<OptiGame.Platform.Gpu.NvidiaSmiProvider>();
    var samples = await smi.SampleAsync(TimeSpan.FromSeconds(seconds), CancellationToken.None);
    foreach (var s in samples) Console.WriteLine(s);
    var health = OptiGame.Core.Gpu.GpuSampling.Summarize(samples);
    Console.WriteLine(health is null ? "Moins de 5 relevés : pas de résumé." : OptiGame.Core.Rating.GameRatings.GpuHealthText(health));
    return;
}

// --disks : disque de chaque lecteur et de chaque profil (type, espace libre, conseil affiché sur la page du jeu).
if (args.Length == 1 && args[0] == "--disks")
{
    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
    {
        var facts = OptiGame.Platform.Storage.GameDiskReader.Read(drive.RootDirectory.FullName);
        Console.WriteLine(facts is null ? $"{drive.Name} : illisible"
            : $"{facts.Drive} {facts.Model} : MediaType={facts.MediaType} BusType={facts.BusType} pénalité={facts.SeekPenalty?.ToString() ?? "?"} " +
              $"TRIM={facts.Trim?.ToString() ?? "?"} → {OptiGame.Core.Library.GameDisk.KindOf(facts)}");
    }
    var profiles = new OptiGame.Core.Profiles.ProfileStore(
        new JsonStateStore<OptiGame.Core.Profiles.ProfilesDocument>(AppPaths.Default.Profiles));
    foreach (var profile in profiles.GetAll())
    {
        var facts = OptiGame.Platform.Storage.GameDiskReader.Read(profile.ExePath);
        if (facts is null)
        {
            Console.WriteLine($"{profile.Name} : lecteur introuvable");
            continue;
        }
        var report = OptiGame.Core.Library.GameDisk.Assess(facts, profile.SteamAppId is not null);
        Console.WriteLine($"{profile.Name} : [{report.Level}] {report.Summary} {report.Detail}");
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
