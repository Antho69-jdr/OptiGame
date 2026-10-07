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

// --update-check [--download] : ce que voit la mise à jour automatique (dernière version publiée sur GitHub, installeur,
// empreinte SHA-256 de GitHub) et la clé de désinstallation de la copie installée. --download : télécharge et vérifie
// l'installeur dans un dossier temporaire (supprimé ensuite), sans rien installer.
if (args.Length is 1 or 2 && args[0] == "--update-check" && (args.Length == 1 || args[1] == "--download"))
{
    Console.WriteLine($"Copie installée (clé de désinstallation) : {OptiGame.Platform.Updates.InstalledCopy.InstallLocation() ?? "aucune"}");
    var client = services.GetRequiredService<OptiGame.Platform.Updates.AppUpdateClient>();
    var result = await client.CheckAsync(new Version(0, 0, 0)); // comparé à 0.0.0 : toute version publiée est détaillée
    Console.WriteLine($"Dernière version publiée : {result.Status} — {result.Message}");
    if (result.Package is { } package)
    {
        Console.WriteLine($"  {package.Tag}, publiée le {package.PublishedAt:dd/MM/yyyy HH:mm} — {package.PageUrl}");
        Console.WriteLine($"  {package.InstallerName} : {package.InstallerSize:N0} octets, SHA-256 {package.Sha256}\n  {package.InstallerUrl}");
        if (args.Length == 2)
        {
            var folder = Path.Combine(Path.GetTempPath(), "OptiGame.DiagDump", "updates");
            var file = await client.DownloadAsync(package, folder, null, CancellationToken.None);
            Console.WriteLine($"  Téléchargé et vérifié (taille, SHA-256, serveurs de GitHub) : {file}");
            File.Delete(file);
        }
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

// --nvidia-settings [mot…] : réglages que le pilote NVIDIA installé connaît (identifiant + nom donné par le pilote), filtrés par
// mots ; valeur appliquée à chaque jeu de Mes jeux pour les réglages trouvés (lecture seule).
if (args.Length >= 1 && args[0] == "--nvidia-settings")
{
    var words = args.Skip(1).ToList();
    var all = OptiGame.Platform.Gpu.NvidiaProfiles.AvailableSettings();
    var found = all.Where(s => words.Count == 0 || words.Any(w => s.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    Console.WriteLine($"{all.Count} réglages connus du pilote ; {found.Count} retenus.");
    var games = new OptiGame.Core.Profiles.ProfileStore(new JsonStateStore<OptiGame.Core.Profiles.ProfilesDocument>(AppPaths.Default.Profiles)).GetAll();
    foreach (var (id, name) in found)
    {
        Console.WriteLine($"0x{id:X8}  {name}");
        try
        {
            if (OptiGame.Platform.Gpu.NvidiaProfiles.AvailableValues(id) is { } admitted)
            {
                Console.WriteLine($"    défaut 0x{admitted.Default:X8} ; valeurs : {string.Join(", ", admitted.Values.Select(v => $"0x{v:X8}"))}");
            }
        }
        catch (OptiGame.Platform.Gpu.NvidiaApiException ex) { Console.WriteLine($"    valeurs : {ex.Message}"); }
        if (words.Count == 0) continue;
        foreach (var game in games)
        {
            var value = OptiGame.Platform.Gpu.NvidiaProfiles.Read(game.ExePath, id);
            if (value.Value is not null || value.EffectiveValue is not null)
            {
                Console.WriteLine($"    {game.Name} : propre au profil = {(value.Value is { } v ? $"0x{v:X8}" : "—")}, appliqué = {(value.EffectiveValue is { } e ? $"0x{e:X8}" : "défaut")}");
            }
        }
    }
    return;
}

// --nvidia-release-notes <version>… [--excerpt <dossier>] : problèmes encore ouverts lus dans le PDF des notes de version de
// NVIDIA, et avis de confiance pour les jeux de « Mes jeux » (lecture seule). --excerpt : texte de la section seule, pour les tests.
if (args.Length >= 2 && args[0] == "--nvidia-release-notes")
{
    var excerptDir = Array.IndexOf(args, "--excerpt") is var e and >= 0 && e + 1 < args.Length ? args[e + 1] : null;
    var profiles = new OptiGame.Core.Profiles.ProfileStore(
        new JsonStateStore<OptiGame.Core.Profiles.ProfilesDocument>(AppPaths.Default.Profiles));
    var games = profiles.GetAll().Select(p => p.Name).ToList();
    using var http = new System.Net.Http.HttpClient();
    foreach (var version in args.Skip(1).TakeWhile(a => a != "--excerpt"))
    {
        var url = new Uri($"https://us.download.nvidia.com/Windows/{version}/{version}-win11-win10-release-notes.pdf");
        var text = OptiGame.Core.Drivers.PdfText.Extract(await http.GetByteArrayAsync(url));
        var issues = OptiGame.Core.Drivers.NvidiaReleaseNotes.OpenIssues(text, version);
        Console.WriteLine($"{version} : {(issues is null ? "section introuvable" : $"{issues.Count} problème(s) ouvert(s)")}");
        foreach (var issue in issues ?? []) Console.WriteLine($"  > {issue}");
        var driver = new OptiGame.Core.Drivers.NvidiaDriver("GeForce Game Ready Driver", version, null, url, null, null, []);
        var confidence = OptiGame.Core.Drivers.DriverConfidences.Evaluate(driver, issues, games, DateOnly.FromDateTime(DateTime.Today));
        Console.WriteLine($"  Avis pour vos {games.Count} jeux : {confidence.Headline}");
        if (excerptDir is not null)
        {
            var start = text.LastIndexOf($"Open Issues in Version {version}", StringComparison.Ordinal);
            var end = text.IndexOf("Issues Not Caused by NVIDIA Drivers", start, StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(excerptDir, $"nvidia-open-issues-{version}.txt"), text[start..(end + 35)]);
        }
    }
    return;
}

// --background [secondes] : programmes qui prennent du processeur, comme pendant la mesure automatique (deux relevés, lecture seule).
if (args.Length >= 1 && args[0] == "--background")
{
    var seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 10;
    var before = OptiGame.Platform.Measurement.ProcessCpuSampler.Snapshot();
    var watch = System.Diagnostics.Stopwatch.StartNew();
    Console.WriteLine($"{before.Count} processus dans la session ; second relevé dans {seconds} s…");
    Thread.Sleep(TimeSpan.FromSeconds(seconds));
    var after = OptiGame.Platform.Measurement.ProcessCpuSampler.Snapshot();
    var busy = OptiGame.Core.Measurement.BackgroundLoad.Summarize(before, after, watch.Elapsed, Environment.ProcessorCount, "",
        OptiGame.Platform.Measurement.ProcessCpuSampler.IsWindowsComponent);
    Console.WriteLine(busy.Count == 0
        ? $"Aucun programme au-dessus de {OptiGame.Core.Measurement.BackgroundLoad.MinimumPercent} % du processeur ({Environment.ProcessorCount} threads)."
        : $"Programmes gourmands ({Environment.ProcessorCount} threads) : {OptiGame.Core.Measurement.BackgroundLoad.Describe(busy)}");
    foreach (var p in busy) Console.WriteLine($"  {p.ExeName} : {p.CpuPercent} % — {p.Path}");
    return;
}

// --ingame-settings : réglages lus dans les fichiers de chaque jeu (Unreal Engine, Unity), lecture seule.
if (args.Length == 1 && args[0] == "--ingame-settings")
{
    var profiles = new OptiGame.Core.Profiles.ProfileStore(
        new JsonStateStore<OptiGame.Core.Profiles.ProfilesDocument>(AppPaths.Default.Profiles));
    foreach (var profile in profiles.GetAll())
    {
        var settings = OptiGame.Platform.InGame.InGameSettingsReader.Read(profile.ExePath);
        Console.WriteLine(settings is null
            ? $"{profile.Name} : rien de lisible (autre moteur, ou jeu jamais lancé)"
            : $"{profile.Name} : {settings.Description(DateTime.Now)}\n    {settings.SourcePath}" +
              (settings.PresetDetail is { } detail ? $"\n    {detail}" : ""));
    }
    return;
}

// --steam-owned : jeux Steam possédés d'après les caches du client (appinfo.vdf + packageinfo.vdf), lecture seule.
if (args.Length == 1 && args[0] == "--steam-owned")
{
    var cache = Path.Combine(OptiGame.Platform.Library.GameLibraryScanner.SteamPath() ?? @"C:\Program Files (x86)\Steam", "appcache");
    var apps = OptiGame.Core.Library.SteamBinaryCache.ParseAppInfo(File.ReadAllBytes(Path.Combine(cache, "appinfo.vdf")));
    var packages = OptiGame.Core.Library.SteamBinaryCache.ParsePackageInfo(File.ReadAllBytes(Path.Combine(cache, "packageinfo.vdf")));
    Console.WriteLine($"appinfo : {apps.Count} applications ; packageinfo : {packages.Count} paquets");
    foreach (var g in apps.GroupBy(a => a.Type).OrderByDescending(g => g.Count())) Console.WriteLine($"  type « {g.Key} » : {g.Count()}");
    var licensed = packages.SelectMany(p => p.AppIds).ToHashSet();
    var games = apps.Where(a => a.Type == "game" && licensed.Contains(a.AppId)).OrderBy(a => a.Name).ToList();
    var library = Directory.Exists(Path.Combine(cache, "librarycache"))
        ? Directory.GetDirectories(Path.Combine(cache, "librarycache")).Select(d => uint.TryParse(Path.GetFileName(d), out var id) ? id : 0).ToHashSet()
        : [];
    Console.WriteLine($"Jeux sous licence : {games.Count} (dont {games.Count(g => library.Contains(g.AppId))} présents dans librarycache) ; paquet 0 : {packages.FirstOrDefault(p => p.PackageId == 0)?.AppIds.Count ?? 0} applications");
    foreach (var g in games.Where(g => !library.Contains(g.AppId))) Console.WriteLine($"  absent de librarycache : {g.AppId} {g.Name}");
    var package0 = packages.FirstOrDefault(p => p.PackageId == 0)?.AppIds.ToHashSet() ?? [];
    foreach (var g in games.Where(g => package0.Contains(g.AppId))) Console.WriteLine($"  dans le paquet 0 : {g.AppId} {g.Name}");
    foreach (var g in games.Take(5)) Console.WriteLine($"  {g.AppId,8} {g.Name}  genres=[{string.Join(",", g.Genres)}] catégories=[{string.Join(",", g.Categories)}]");
    // Un jeu représentatif par genre et par catégorie (pour vérifier leurs noms sur le magasin).
    Console.WriteLine("GENRES " + string.Join(" ", games.SelectMany(g => g.Genres.Select(id => (id, g.AppId))).GroupBy(x => x.id).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.First().AppId}({x.Count()})")));
    Console.WriteLine("CATEGORIES " + string.Join(" ", games.SelectMany(g => g.Categories.Select(id => (id, g.AppId))).GroupBy(x => x.id).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.First().AppId}({x.Count()})")));
    return;
}

// --steam-cache-sample <dossier> <appid…> : extrait réel et réduit d'appinfo.vdf / packageinfo.vdf pour les tests
// (les applications demandées + la table de noms entière ; le paquet 0 et les paquets qui contiennent ces applications).
if (args.Length >= 3 && args[0] == "--steam-cache-sample")
{
    var cache = Path.Combine(OptiGame.Platform.Library.GameLibraryScanner.SteamPath() ?? @"C:\Program Files (x86)\Steam", "appcache");
    var wanted = args.Skip(2).Select(uint.Parse).ToHashSet();
    var appData = File.ReadAllBytes(Path.Combine(cache, "appinfo.vdf"));
    var (entries, table) = OptiGame.Core.Library.SteamBinaryCache.AppEntries(appData);
    using (var output = new MemoryStream())
    {
        var body = entries.Where(e => wanted.Contains(e.AppId)).SelectMany(e => appData[e.Start..e.End]).ToArray();
        output.Write(appData, 0, 8); // magic, univers
        output.Write(BitConverter.GetBytes((long)(16 + body.Length + 4)), 0, 8); // nouvelle position de la table de noms
        output.Write(body);
        output.Write(new byte[4]); // appid 0 : fin des entrées
        output.Write(appData, table, appData.Length - table);
        File.WriteAllBytes(Path.Combine(args[1], "appinfo-excerpt.vdf"), output.ToArray());
    }
    var packageData = File.ReadAllBytes(Path.Combine(cache, "packageinfo.vdf"));
    using (var output = new MemoryStream())
    {
        output.Write(packageData, 0, 8);
        foreach (var p in OptiGame.Core.Library.SteamBinaryCache.PackageEntries(packageData).Where(p => p.Id == 0 || p.AppIds.Any(wanted.Contains)))
        {
            output.Write(packageData, p.Start, p.End - p.Start);
        }
        output.Write(BitConverter.GetBytes(uint.MaxValue));
        File.WriteAllBytes(Path.Combine(args[1], "packageinfo-excerpt.vdf"), output.ToArray());
    }
    Console.WriteLine("Extraits écrits.");
    return;
}

// --store-owned : jeux possédés non installés des autres magasins (catalogue Epic + copie de la base de GOG Galaxy), lecture seule.
if (args.Length == 1 && args[0] == "--store-owned")
{
    var owned = new OptiGame.Platform.Library.StoreOwnedLibrary(services.GetRequiredService<OptiGame.Core.Logging.FileLog>()).ReadNotInstalled();
    foreach (var group in owned.GroupBy(g => g.Store)) Console.WriteLine($"{group.Key} : {group.Count()} jeux non installés");
    foreach (var g in owned.Where(g => g.Store != OptiGame.Core.Library.GameSource.Epic).Concat(owned.Where(g => g.Store == OptiGame.Core.Library.GameSource.Epic).Take(5)))
    {
        Console.WriteLine($"  [{g.Store}] {g.Name} | {string.Join(", ", g.Genres)} | {g.Key} | {(g.CoverUrl is null ? "pas de jaquette" : "jaquette")}");
    }
    return;
}

// --igdb-taxonomy <nom>… : genres et types IGDB (requête multiple, 10 noms max), tels que Mes jeux les filtrera ; OPTIGAME_RAW=1 :
// réponse brute de la requête multiple. Seuls les noms
// sont envoyés ; identifiants IGDB de Paramètres.
if (args.Length >= 2 && args[0] == "--igdb-taxonomy")
{
    var names = args.Skip(1).Take(OptiGame.Core.Artwork.Igdb.MaxQueriesPerMultiQuery).ToList();
    // Identifiants lus (jamais écrits) dans le vrai settings.json de l'appli.
    var realSettings = new OptiGame.Core.Settings.AppSettingsStore(new OptiGame.Core.State.JsonStateStore<OptiGame.Core.Settings.AppSettings>(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiGame", "settings.json")));
    var igdb = new OptiGame.Platform.Artwork.IgdbClient(realSettings, services.GetRequiredService<OptiGame.Core.Logging.FileLog>());
    if (Environment.GetEnvironmentVariable("OPTIGAME_RAW") == "1")
    {
        Console.WriteLine(await igdb.MultiQueryAsync(OptiGame.Core.Artwork.Igdb.TaxonomyMultiQuery(names)));
        return;
    }
    var tags = await igdb.TaxonomyAsync(names);
    for (var i = 0; i < names.Count; i++)
    {
        Console.WriteLine(tags[i] is { } t
            ? $"{names[i]} : genres [{string.Join(", ", t.Genres)}] · types [{string.Join(", ", t.Kinds.Select(OptiGame.Core.Library.SteamTaxonomy.Label))}]"
            : $"{names[i]} : introuvable sous ce nom");
    }
    return;
}

// --store-page <epic|gog> <espace de noms ou id produit> <titre> : adresse que « Voir sur Epic Games / GOG » ouvrirait (rien n'est ouvert).
if (args.Length == 4 && args[0] == "--store-page")
{
    var store = args[1] == "epic" ? OptiGame.Core.Library.GameSource.Epic : OptiGame.Core.Library.GameSource.Gog;
    var resolver = new OptiGame.Platform.Library.StorePageResolver(services.GetRequiredService<OptiGame.Core.Logging.FileLog>());
    Console.WriteLine(await resolver.UrlAsync(new OptiGame.Core.Library.StoreProduct(store, args[2], args[3])));
    return;
}

// --galaxy-meta : « originalMeta » de quelques jeux GOG dans la base de GOG Galaxy (lue sur une copie, jamais en place) : quels
// champs (genres, thèmes, modes de jeu…) Galaxy fournit vraiment.
if (args.Length == 1 && args[0] == "--galaxy-meta")
{
    var copy = Path.Combine(Path.GetTempPath(), "OptiGame-galaxy-meta");
    Directory.CreateDirectory(copy);
    try
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var source = OptiGame.Platform.Library.StoreOwnedLibrary.GalaxyDatabasePath + suffix;
            if (!File.Exists(source)) continue;
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = File.Create(Path.Combine(copy, "galaxy-2.0.db" + suffix));
            input.CopyTo(output);
        }
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(copy, "galaxy-2.0.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            select gp.releaseKey || ' [' || t.type || ']', substr(gp.value, 1, 300) from GamePieces gp join GamePieceTypes t on t.id = gp.gamePieceTypeId
            where gp.releaseKey in (select releaseKey from GamePieces where releaseKey like 'gog!_%' escape '!' limit 1)
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read()) Console.WriteLine($"{reader.GetString(0)} : {reader.GetString(1)}");
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(copy, recursive: true);
    }
    return;
}

// --memory : coût en mémoire de chaque lecture lourde de « Mes jeux », mesurée seule (lecture seule), puis des jaquettes
// décodées comme dans la section « non installés » (même code : ImageLoader de l'appli, lié à cet outil).
if (args.Length == 1 && args[0] == "--memory")
{
    Console.WriteLine($"Au départ : {OptiGame.Core.Logging.MemoryUsage.Now().Describe()}");
    var appCache = OptiGame.Platform.Library.GameLibraryScanner.SteamPath() is { } steamDir ? Path.Combine(steamDir, "appcache") : null;
    var sources = new[]
    {
        appCache is null ? null : Path.Combine(appCache, "appinfo.vdf"),
        appCache is null ? null : Path.Combine(appCache, "packageinfo.vdf"),
        OptiGame.Platform.Library.StoreOwnedLibrary.EpicCatalogPath,
        OptiGame.Platform.Library.StoreOwnedLibrary.GalaxyDatabasePath,
        OptiGame.Platform.Library.StoreOwnedLibrary.GalaxyDatabasePath + "-wal",
    };
    foreach (var file in sources.OfType<string>())
    {
        Console.WriteLine($"  {file} : {(File.Exists(file) ? OptiGame.Core.Logging.MemoryUsage.Mb(new FileInfo(file).Length) + " Mo" : "absent")}");
    }

    var owned = (OptiGame.Platform.Library.SteamOwnedSnapshot?)MeasureRead(
        "Steam, jeux possédés (appinfo.vdf + packageinfo.vdf)", OptiGame.Platform.Library.SteamOwnedLibrary.Read);
    var steamApps = (IReadOnlyList<OptiGame.Platform.Library.GameLibraryScanner.SteamApp>)MeasureRead(
        "Steam, jeux installés (manifestes)", OptiGame.Platform.Library.GameLibraryScanner.SteamApps)!;
    MeasureRead("Epic + GOG, jeux possédés non installés",
        () => new OptiGame.Platform.Library.StoreOwnedLibrary(services.GetRequiredService<OptiGame.Core.Logging.FileLog>()).ReadNotInstalled());

    // Jaquettes de la section « non installés » : Steam (cache du client) + Epic / GOG déjà téléchargées par OptiGame.
    var installedIds = steamApps.Select(a => a.AppId).ToHashSet();
    var storeCovers = Path.Combine(AppPaths.Default.Root, "covers", "stores");
    var covers = (owned?.Games.Values ?? [])
        .Where(g => !installedIds.Contains(g.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        .Select(g => OptiGame.Platform.Library.SteamOwnedLibrary.CoverPath(g.AppId))
        .OfType<string>()
        .Concat(Directory.Exists(storeCovers) ? Directory.EnumerateFiles(storeCovers) : [])
        .ToList();
    MeasureImages(covers);
    Console.WriteLine($"À la fin : {OptiGame.Core.Logging.MemoryUsage.Now().Describe()}");
    return;

    // Temps, mémoire allouée pendant la lecture (déchets compris) et mémoire encore occupée après un nettoyage complet.
    static object? MeasureRead(string what, Func<object?> read)
    {
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = read();
        watch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var retained = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(result);
        Console.WriteLine($"{what} : {watch.ElapsedMilliseconds} ms, {OptiGame.Core.Logging.MemoryUsage.Mb(allocated)} Mo alloués pendant la lecture, " +
                          $"{OptiGame.Core.Logging.MemoryUsage.Mb(Math.Max(0, retained))} Mo encore occupés ensuite");
        return result;
    }

    // Mémoire privée ajoutée par le décodage (WPF garde les pixels hors du tas .NET), en gris puis en couleur, images gardées
    // comme le font les jaquettes affichées.
    static void MeasureImages(IReadOnlyList<string> files)
    {
        if (files.Count == 0)
        {
            Console.WriteLine("Jaquettes : aucune trouvée.");
            return;
        }
        var thread = new Thread(() =>
        {
            var gray = Decode(files, "grisées (section « non installés »)", gray: true);
            var color = Decode(files, "en couleur (grille « Mes jeux »)", gray: false);
            if (gray.Count > 0)
            {
                var pixels = gray.Sum(i => (long)i.PixelWidth * i.PixelHeight) / gray.Count;
                Console.WriteLine($"  repères par jaquette : pixels gris seuls ≈ {pixels / 1024} Ko, en couleur ≈ {pixels * 4 / 1024} Ko");
            }
            GC.KeepAlive(gray);
            GC.KeepAlive(color);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    static List<System.Windows.Media.Imaging.BitmapSource> Decode(IReadOnlyList<string> files, string what, bool gray)
    {
        var before = PrivateBytes();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var width = OptiGame.App.Converters.ImageLoader.PixelsFor(OptiGame.App.Converters.ImageLoader.GridCoverWidth); // écran à 100 %
        var images = files.Select(f => OptiGame.App.Converters.ImageLoader.Load(f, width, gray)).OfType<System.Windows.Media.Imaging.BitmapSource>().ToList();
        watch.Stop();
        var added = PrivateBytes() - before;
        Console.WriteLine($"Jaquettes {what}, {width} px de large : {images.Count} décodées en {watch.ElapsedMilliseconds} ms, +{OptiGame.Core.Logging.MemoryUsage.Mb(added)} Mo " +
                          $"de mémoire privée, soit ≈ {(images.Count == 0 ? 0 : added / images.Count / 1024)} Ko par jaquette");
        return images;
    }

    static long PrivateBytes()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }
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

        // DLSS le plus récent : deux réglages dans le même profil, puis annulation (profil factice supprimé s'il est vide).
        string ShowDlss()
        {
            var o = OptiGame.Platform.Gpu.NvidiaProfiles.Read(exe, OptiGame.Core.Gpu.DlssOverride.OverrideId);
            var p = OptiGame.Platform.Gpu.NvidiaProfiles.Read(exe, OptiGame.Core.Gpu.DlssOverride.PresetId);
            return $"profil={o.ProfileName ?? "(aucun)"} remplacement={o.Value?.ToString() ?? "-"} préréglage={(p.Value is { } v ? $"0x{v:X}" : "-")} " +
                   $"→ {OptiGame.Core.Gpu.DlssOverride.Describe(o.EffectiveValue, p.EffectiveValue)}";
        }
        Console.WriteLine($"DLSS avant   : {ShowDlss()}");
        journal.Apply(OptiGame.Core.Gpu.DlssOverride.Change(id, "test", exe, null, "test"));
        Console.WriteLine($"DLSS imposé  : {ShowDlss()}");
        var dlssReport = journal.Undo(OptiGame.Core.Gpu.DlssOverride.ChangeId(id));
        Console.WriteLine($"DLSS annulé  : {ShowDlss()} (succès : {dlssReport.Success})");
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
