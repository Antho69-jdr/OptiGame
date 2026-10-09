using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using OptiGame.App.Services;
using OptiGame.App.ViewModels;
using OptiGame.App.Views;
using OptiGame.Core.Profiles;

namespace OptiGame.App.Snapshots;

/// <summary>
/// Mode de développement « --snapshot &lt;dossier&gt; » : chaque page de la fenêtre principale est rendue en PNG, à plusieurs
/// tailles de fenêtre, pour comparer l'interface avant / après une modification (scripts\ui-snapshots.ps1). Lecture seule :
/// ni détection des jeux, ni reprise de session, ni dock, ni icône de notification, ni mise à jour. Refusé sans
/// OPTIGAME_DATA_DIR, pour ne jamais tourner sur les vraies données.
/// </summary>
internal static class PageSnapshots
{
    public const string Argument = "--snapshot";
    private const string SizesArgument = "--sizes";
    private const string GameArgument = "--game";
    private const string OnlyArgument = "--only";

    /// <summary>Tailles par défaut (DIP) : défaut actuel, minimum, zone de travail à 150 % d'un écran 1080p, grand écran.</summary>
    private static readonly Size[] DefaultSizes = [new(1240, 860), new(880, 600), new(1280, 680), new(1600, 1000)];

    public static bool IsRequested(string[] args) => args.Contains(Argument, StringComparer.OrdinalIgnoreCase);

    public static async Task RunAsync(IServiceProvider services, string[] args)
    {
        var output = ValueAfter(args, Argument) ?? throw new ArgumentException($"{Argument} <dossier> attendu.");
        Directory.CreateDirectory(output);
        var report = new StringBuilder();
        void Log(string line)
        {
            report.AppendLine($"{DateTime.Now:HH:mm:ss} {line}");
            File.WriteAllText(Path.Combine(output, "snapshot.log"), report.ToString(), Encoding.UTF8);
        }

        try
        {
            var sizes = ParseSizes(ValueAfter(args, SizesArgument)) ?? DefaultSizes;
            // --only 7,0 : seulement les captures dont le nom commence par l'un de ces préfixes (pages, dialogues, galerie).
            var only = ValueAfter(args, OnlyArgument)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool Wanted(string name) => only is null || only.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            var main = services.GetRequiredService<MainViewModel>();
            var library = services.GetRequiredService<LibraryViewModel>();
            var window = services.GetRequiredService<MainWindow>();
            // Hors des écrans, sans activation ni bouton de barre des tâches : rien ne vient gêner l'utilisateur.
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -32000;
            window.Top = -32000;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Width = sizes[0].Width;
            window.Height = sizes[0].Height;
            window.Show();

            var pages = new List<(string Name, Func<Task> Open)>
            {
                ("1-mes-jeux", async () => { main.Navigate(library); await Settle(4000); }),
                ("1b-mes-jeux-aucun-resultat", async () => { main.Navigate(library); library.SearchText = "zzzz"; await Settle(1200); }),
                ("1c-mes-jeux-epic", async () =>
                {
                    main.Navigate(library);
                    library.SearchText = "";
                    library.SelectedStore = library.StoreOptions[2];
                    await Settle(2500);
                }),
                ("1d-mes-jeux-filtres-effaces", async () => { library.ClearFiltersCommand.Execute(null); await Settle(800); }),
                // Taille des jaquettes (Paramètres › Mes jeux) : grandes puis petites, dans la copie des données ; remise à la fin (1g).
                ("1f-mes-jeux-grandes-jaquettes", async () =>
                {
                    services.GetRequiredService<Core.Settings.AppSettingsStore>().Update(s => s.CoverSize = Core.Settings.CoverSize.Large);
                    main.Navigate(library);
                    await Settle(2500);
                }),
                ("1g-mes-jeux-petites-jaquettes", async () =>
                {
                    var store = services.GetRequiredService<Core.Settings.AppSettingsStore>();
                    store.Update(s => s.CoverSize = Core.Settings.CoverSize.Small);
                    await Settle(2500);
                    Log($"Jaquettes : {Converters.ImageLoader.Describe()}");
                }),
                ("1h-mes-jeux-taille-remise", async () =>
                {
                    services.GetRequiredService<Core.Settings.AppSettingsStore>().Update(s => s.CoverSize = Core.Settings.CoverSize.Medium);
                    await Settle(1500);
                }),
                // Contrôle des liaisons : chaque bouton visible de chaque page doit avoir sa commande (une liaison en erreur laisse le
                // bouton sans effet, et son clic retombe sur son parent — la jaquette lançait l'installation, constaté le 2026-10-06).
                ("1z-liaisons-des-boutons", async () =>
                {
                    static IEnumerable<DependencyObject> Tree(DependencyObject root)
                    {
                        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                        {
                            var child = VisualTreeHelper.GetChild(root, i);
                            yield return child;
                            foreach (var d in Tree(child)) yield return d;
                        }
                    }
                    var pages = new (string Name, object Page)[]
                    {
                        ("Mes jeux", library), ("Diagnostic", main.Diagnostic), ("Pilotes", main.NavItems[2].Page), ("Mesures", main.NavItems[3].Page),
                        ("Appel", main.Call),
                        ("Paramètres", main.Settings),
                    };
                    library.ShowUninstalled = true;
                    var broken = 0;
                    foreach (var (name, page) in pages)
                    {
                        main.Navigate(page);
                        await Settle(3000);
                        // Actions de survol des jaquettes affichées, pour que leurs liaisons se fassent.
                        foreach (var tile in Tree(window).OfType<Controls.CoverTile>().Take(6).ToList())
                        {
                            if (tile.Template.FindName("Actions", tile) is UIElement actions) actions.Visibility = Visibility.Visible;
                        }
                        await Settle(800);
                        var buttons = Tree(window).OfType<System.Windows.Controls.Primitives.ButtonBase>().ToList();
                        foreach (var b in buttons)
                        {
                            var binding = System.Windows.Data.BindingOperations.GetBindingExpression(b, System.Windows.Controls.Primitives.ButtonBase.CommandProperty);
                            if (binding is null || binding.Status == System.Windows.Data.BindingStatus.Active) continue;
                            broken++;
                            Log($"{name} : bouton « {b.Content ?? (b as Controls.IconButton)?.Label} » ({b.DataContext?.GetType().Name}) : liaison {binding.Status} ({binding.ParentBinding.Path?.Path})");
                        }
                        Log($"{name} : {buttons.Count} boutons contrôlés");
                    }
                    Log(broken == 0 ? "Liaisons : toutes actives." : $"Liaisons : {broken} en erreur.");
                }),
                // Genres IGDB (requêtes réelles si les identifiants sont copiés, cache dans le dossier de test) : Epic + « Tir ».
                ("1e-mes-jeux-genre-epic", async () =>
                {
                    main.Navigate(library);
                    var tags = services.GetRequiredService<GameTagService>();
                    await Settle(3000);
                    await WaitUntil(() => !tags.IsBusy, 300_000);
                    library.ShowUninstalled = true;
                    library.SelectedStore = library.StoreOptions.First(o => o.Store == Core.Library.GameSource.Epic);
                    library.SelectedGenre = library.GenreOptions.FirstOrDefault(o => o.Name == "Tir") ?? library.GenreOptions[0];
                    Log($"Genres proposés : {string.Join(", ", library.GenreOptions.Skip(1).Select(o => o.Name))}");
                    await Settle(2500);
                }),
                ("2-diagnostic", async () => { main.Navigate(main.Diagnostic); await WaitUntil(() => !main.Diagnostic.IsBusy && main.Diagnostic.HasResults, 60_000); }),
                // Diagnostic avec des résultats d'exemple (à corriger, non vérifié, facultatif) : présentés seulement, rien n'est appliqué.
                ("2b-diagnostic-exemple", async () =>
                {
                    main.Navigate(main.Diagnostic);
                    main.Diagnostic.Present(SampleDiagnostic());
                    await Settle(800);
                }),
                ("3-pilotes", async () =>
                {
                    var drivers = main.NavItems[2].Page;
                    main.Navigate(drivers);
                    await Settle(500);
                    await WaitUntil(() => drivers is DriversViewModel { IsSearching: false }, 90_000);
                }),
                ("4-mesures", async () => { main.Navigate(main.Measures); await Settle(1500); }),
                ("4b-mesures-comparaison", async () =>
                {
                    main.Navigate(main.Measures);
                    main.Measures.UpdateSelection(main.Measures.Captures.Take(1).ToList());
                    Log($"Résumé d'une mesure : {main.Measures.SummaryText()}");
                    main.Measures.UpdateSelection(main.Measures.Captures.Take(2).ToList());
                    Log($"Résumé de deux mesures : {main.Measures.SummaryText()}");
                    await Settle(1500);
                }),
                ("5-parametres", async () => { main.Settings.SelectedTab = SettingsTab.General; main.Navigate(main.Settings); await Settle(1500); }),
                ("5b-parametres-mes-jeux", async () => { main.Settings.SelectedTab = SettingsTab.Games; main.Navigate(main.Settings); await Settle(800); }),
                ("5c-parametres-dock", async () => { main.Settings.SelectedTab = SettingsTab.Dock; main.Navigate(main.Settings); await Settle(800); }),
                ("5d-parametres-mesures", async () => { main.Settings.SelectedTab = SettingsTab.Measures; main.Navigate(main.Settings); await Settle(800); }),
                ("5e-parametres-mises-a-jour", async () => { main.Settings.SelectedTab = SettingsTab.Updates; main.Navigate(main.Settings); await Settle(800); }),
                ("5f-parametres-donnees", async () => { main.Settings.SelectedTab = SettingsTab.Data; main.Navigate(main.Settings); await Settle(800); }),
            };
            // Avis de confiance d'exemple sur la carte graphique (en mémoire : la vraie recherche peut trouver le pilote à jour).
            pages.Add(("3b-pilotes-avis", async () =>
            {
                var drivers = (DriversViewModel)main.NavItems[2].Page;
                main.Navigate(drivers);
                await WaitUntil(() => !drivers.IsSearching, 90_000);
                if (drivers.Gpus.FirstOrDefault() is { } gpu)
                {
                    var sample = new Core.Drivers.NvidiaDriver("GeForce Game Ready Driver", "617.14", new DateOnly(2026, 10, 6),
                        new Uri("https://us.download.nvidia.com/x.exe"), null, null, [], "CONTROL Resonant & AION 2",
                        ["Portal 2 may crash when loading a save"]);
                    gpu.Confidence = Core.Drivers.DriverConfidences.Evaluate(sample,
                        ["PUBG: BATTLEGROUNDS may stutter after extended gameplay", "\"Prefer Maximum Performance\" may not be applied correctly"],
                        ["PUBG: BATTLEGROUNDS", "Portal 2"], new DateOnly(2026, 10, 7));
                    gpu.Impact = new Core.Drivers.DriverImpactReport("616.56", "617.42",
                        [new("Overwatch", 162, 151, 3, 2), new("Void Crew", 118, 121, 2, 2)]);
                }
                await Settle(800);
            }));
            // Coque avec alertes et badges d'exemple (rien n'est écrit : alertes en mémoire, compteurs remis ensuite par l'analyse).
            pages.Add(("8-coque-alertes", async () =>
            {
                var alerts = services.GetRequiredService<ShellAlerts>();
                alerts.Show(new ShellAlert("demo-detection", Controls.Severity.Error, "Détection des jeux indisponible",
                    "Les réglages de partie ne seront pas appliqués automatiquement : accès refusé à WMI.") { IsClosable = false, ShowsLog = true });
                alerts.Show(new ShellAlert("demo-session", Controls.Severity.Warning, "Partie de Portal 2 : optimisation partielle",
                    "Discord.exe n'a pas pu être fermé."));
                main.Diagnostic.ProblemCount = 3;
                ((DriversViewModel)main.NavItems[2].Page).AvailableCount = 1;
                main.Navigate(main.Diagnostic);
                await Settle(800);
            }));
            if (PickGame(services.GetRequiredService<ProfileStore>(), ValueAfter(args, GameArgument)) is { } game)
            {
                pages.Add(("6-fiche-du-jeu", async () => { main.Navigate(library); library.ShowGame(game.Id); await Settle(4000); }));
                pages.Add(("6b-fiche-optimisation", async () => { library.OpenGame!.SelectedTab = GameTab.Optimization; await Settle(1500); }));
                pages.Add(("6c-fiche-proprietes", async () => { library.OpenGame!.SelectedTab = GameTab.Properties; await Settle(800); }));
                // Menu de l'icône de notification (pas d'icône en mode capture) : les jeux récents qu'il proposerait.
                pages.Add(("6k-menu-notification", async () =>
                {
                    var tray = services.GetRequiredService<TrayViewModel>();
                    tray.RefreshRecentGames();
                    Log($"Menu de notification, jeux récents : {string.Join(" · ", tray.RecentGames.Select(g => g.Header))}");
                    await Settle(100);
                }));
                // Partage : export des réglages de partie, puis import d'un fichier « reçu » (programme protégé, plan inconnu) dans
                // l'éditeur ; rien n'est enregistré (barre « Enregistrer » visible), puis « Abandonner les modifications ».
                pages.Add(("6n-reglages-importes", async () =>
                {
                    var editor = library.OpenGame!.Editor;
                    library.OpenGame.SelectedTab = GameTab.Optimization;
                    var exported = Core.Profiles.SharedGameSettings.From(services.GetRequiredService<Core.Profiles.ProfileStore>().Find(library.OpenGame.Id)!).ToJson();
                    Log($"Export : {exported.Length} caractères, chemin du PC dedans : {(exported.Contains(@":\\") ? "OUI" : "non")}");
                    var received = """
                        { "format": "optigame-reglages-de-partie", "version": 1, "jeu": "Portal 2", "exe": "portal2.exe", "optimiser": true,
                          "planAlimentation": "e9a42b02-d5df-448d-aa00-03f14749eb61", "priorite": "AboveNormal",
                          "programmesAFermer": [ { "exe": "chrome.exe", "relancer": true }, { "exe": "explorer.exe" } ] }
                        """;
                    var (shared, ignored) = Core.Profiles.SharedGameSettings.Parse(received);
                    var notes = editor.ApplySharedSettings(shared, ignored);
                    await Settle(1200);
                    Log($"Import : modifié = {editor.IsDirty}, priorité = {editor.SelectedPriority.Label}, à fermer = {string.Join(", ", editor.ProcessesToClose.Select(p => p.ExeName))} ; écartés : {string.Join(" ; ", notes)}");
                }));
                pages.Add(("6o-import-abandonne", async () =>
                {
                    library.OpenGame!.Editor.RevertCommand.Execute(null);
                    await Settle(800);
                }));
                // Couleur d'accent changée fenêtre ouverte (copie des données), puis remise au vert (6m).
                pages.Add(("6l-accent-en-direct", async () =>
                {
                    services.GetRequiredService<Core.Settings.AppSettingsStore>().Update(s => s.AccentColor = Core.Settings.AccentColor.Violet);
                    library.OpenGame!.SelectedTab = GameTab.Optimization;
                    await Settle(1500);
                }));
                pages.Add(("6m-accent-remis", async () =>
                {
                    services.GetRequiredService<Core.Settings.AppSettingsStore>().Update(s => s.AccentColor = Core.Settings.AccentColor.Green);
                    await Settle(1500);
                }));
                // Réglages lus dans le jeu (base de définitions), liste dépliée.
                pages.Add(("6j-fiche-reglages-du-jeu", async () =>
                {
                    library.OpenGame!.SelectedTab = GameTab.Overview;
                    await Settle(800);
                    static IEnumerable<DependencyObject> All(DependencyObject root)
                    {
                        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                        {
                            var child = VisualTreeHelper.GetChild(root, i);
                            yield return child;
                            foreach (var d in All(child)) yield return d;
                        }
                    }
                    foreach (var expander in All(window).OfType<System.Windows.Controls.Expander>()
                                 .Where(e => e.Header is string h && h.Contains("dans le jeu", StringComparison.Ordinal)))
                    {
                        expander.IsExpanded = true;
                    }
                    await Settle(800);
                }));
                // Bas de la fiche : le fil d'Ariane flotte toujours en haut à gauche.
                pages.Add(("6i-fiche-defilee", async () =>
                {
                    library.OpenGame!.SelectedTab = GameTab.Overview;
                    await Settle(800);
                    if (FindByName<System.Windows.Controls.ScrollViewer>(window, "PageScroll") is { } scroll) scroll.ScrollToEnd();
                    await Settle(800);
                }));
                // Modification non enregistrée (rien n'est enregistré : la fiche est rechargée juste après)
                pages.Add(("6d-fiche-modifiee", async () =>
                {
                    library.OpenGame!.Editor.Name = library.OpenGame.Name + " (test)";
                    library.OpenGame.Editor.NewProcessName = "";
                    await Settle(800);
                }));
                pages.Add(("6e-fiche-retour", async () =>
                {
                    library.OpenGame!.Editor.RevertCommand.Execute(null);
                    library.OpenGame!.SelectedTab = GameTab.Overview;
                    await Settle(800);
                }));
                // Bande-annonce lancée pour de vrai : le journal note la mémoire du moteur web (la vidéo, fenêtre à part, peut
                // manquer sur la capture). Fermée en quittant la fiche, à la fin.
                pages.Add(("6f-fiche-bande-annonce", async () =>
                {
                    library.OpenGame!.SelectedTab = GameTab.Overview;
                    await Settle(1500);
                    static IEnumerable<DependencyObject> All(DependencyObject root)
                    {
                        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                        {
                            var child = VisualTreeHelper.GetChild(root, i);
                            yield return child;
                            foreach (var d in All(child)) yield return d;
                        }
                    }
                    if (All(window).OfType<Controls.TrailerPlayer>().FirstOrDefault(p => p.IsVisible) is { } player)
                    {
                        await player.PlayAsync();
                        await Settle(10_000);
                    }
                    else
                    {
                        Log("Pas de bande-annonce pour ce jeu.");
                    }
                }));
                // Fiche quittée : le moteur web doit être détruit, ses processus fermés (ligne de commande = notre dossier).
                pages.Add(("6g-fiche-bande-annonce-quittee", async () =>
                {
                    main.Navigate(library);
                    library.OpenGame!.BackCommand.Execute(null);
                    await Settle(4000);
                    var folder = Path.Combine(services.GetRequiredService<Core.AppPaths>().Root, "webview");
                    using var search = new System.Management.ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name = 'msedgewebview2.exe'");
                    var left = search.Get().Cast<System.Management.ManagementObject>()
                        .Count(p => (p["CommandLine"] as string)?.Contains(folder, StringComparison.OrdinalIgnoreCase) == true);
                    Log($"Fiche quittée : {left} processus du moteur web encore ouverts.");
                }));
                // Plein écran : la vidéo change de fenêtre (ici hors des écrans, jamais activée) et doit continuer sans redémarrer.
                pages.Add(("6h-fiche-bande-annonce-plein-ecran", async () =>
                {
                    main.Navigate(library);
                    library.ShowGame(game.Id);
                    library.OpenGame!.SelectedTab = GameTab.Overview;
                    await Settle(3000);
                    static IEnumerable<DependencyObject> All(DependencyObject root)
                    {
                        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                        {
                            var child = VisualTreeHelper.GetChild(root, i);
                            yield return child;
                            foreach (var d in All(child)) yield return d;
                        }
                    }
                    if (All(window).OfType<Controls.TrailerPlayer>().FirstOrDefault(p => p.IsVisible) is not { } player)
                    {
                        Log("Pas de bande-annonce pour ce jeu.");
                        return;
                    }
                    await player.PlayAsync();
                    await Settle(6000);
                    Log($"Plein écran, avant : {await player.ProbeAsync()}");
                    player.SetFullScreen(true, offScreen: true);
                    await Settle(3000);
                    Log($"Plein écran, pendant ({(player.IsFullScreen ? "fenêtre plein écran" : "PAS de fenêtre")}) : {await player.ProbeAsync()}");
                    player.SetFullScreen(false, offScreen: true);
                    await Settle(3000);
                    Log($"Plein écran, après retour dans la fiche : {await player.ProbeAsync()}");

                    // Défilement : la vidéo passe sous le fil d'Ariane flottant puis hors de la zone visible ; sa fenêtre doit être découpée.
                    if (All(window).OfType<System.Windows.Controls.ScrollViewer>().FirstOrDefault(s => s.Name == "PageScroll") is { } scroll)
                    {
                        window.Height = 560; // page plus haute que la fenêtre : la vidéo peut passer sous le fil d'Ariane
                        await Settle(800);
                        var top = player.TransformToVisual(scroll).Transform(new Point(0, 0)).Y + scroll.VerticalOffset;
                        Log($"Découpage, vidéo à {top:0} du haut : {player.ClipDescription()}");
                        foreach (var offset in new[] { top - 30, top + 120, top + 600 })
                        {
                            scroll.ScrollToVerticalOffset(Math.Max(0, offset));
                            await Settle(800);
                            Log($"Découpage, défilement {scroll.VerticalOffset:0} (haut de la vidéo à {player.TransformToVisual(scroll).Transform(new Point(0, 0)).Y:0}) : {player.ClipDescription()}");
                        }
                        scroll.ScrollToVerticalOffset(0);
                    }
                    player.Stop();
                }));
            }

            // Appel vocal de bout en bout entre deux appels de ce processus (micro SIMULÉ par le moteur : jamais le vrai micro), par une
            // copie locale du serveur de mise en relation (même protocole ; code valable 8 s ici pour tester l'expiration).
            var host = main.Call;
            CallViewModel? guest = null;
            Call.LocalCallRelay? relay = null;
            int WebProcesses() => System.Diagnostics.Process.GetProcessesByName("msedgewebview2").Length;
            var webBaseline = WebProcesses();
            long WebMemory() => System.Diagnostics.Process.GetProcessesByName("msedgewebview2").Sum(p => { using (p) return p.PrivateMemorySize64; });
            var webMemoryBaseline = WebMemory();
            CallViewModel NewGuest() => new(services.GetRequiredService<Core.AppPaths>(), services.GetRequiredService<Core.Settings.AppSettingsStore>(),
                services.GetRequiredService<Core.Logging.FileLog>()) { Relay = relay!.Url };
            pages.Add(("c1-appel-depart", async () =>
            {
                Call.CallEngine.UseFakeMedia = true;
                relay = new Call.LocalCallRelay(TimeSpan.FromSeconds(8));
                host.Relay = relay.Url;
                host.UseStun = false;
                main.Navigate(host);
                await Settle(800);
            }));
            pages.Add(("c2-appel-code", async () =>
            {
                main.Navigate(host);
                await host.StartCallCommand.ExecuteAsync(null);
                await WaitUntil(() => host.Phase != CallPhase.Preparing, 30_000);
                Log($"Appel, hôte : {host.Phase}, code « {host.Code} », {host.Countdown}, moteur : {WebProcesses() - webBaseline} processus. {host.Status}");
            }));
            pages.Add(("c3-appel-en-cours", async () =>
            {
                guest = NewGuest();
                guest.JoinCode = host.Code.ToLowerInvariant(); // saisi en minuscules : accepté
                var joined = DateTime.Now;
                await guest.JoinCallCommand.ExecuteAsync(null);
                await WaitUntil(() => host.Phase is CallPhase.Connected or CallPhase.Idle && guest.Phase is CallPhase.Connected or CallPhase.Idle, 40_000);
                Log($"Appel : hôte {host.Phase}, invité {guest.Phase} en {(DateTime.Now - joined).TotalSeconds:0.0} s, {relay!.Relayed} messages relayés ; " +
                    $"mots {(host.SafetyWords == guest.SafetyWords && host.SafetyWords.Length > 0 ? "identiques" : "DIFFÉRENTS")} ({host.SafetyWords}). {host.Status}{guest.Status}");
                var silent = guest.BytesReceived;
                await Settle(2000);
                Log($"Micros coupés : l'invité a reçu {guest.BytesReceived - silent} octets en 2 s.");
                host.ToggleMuteCommand.Execute(null);
                guest.ToggleMuteCommand.Execute(null);
                var start = guest.BytesReceived;
                await Settle(3000);
                Log($"Micros ouverts : l'invité a reçu {guest.BytesReceived - start} octets en 3 s, niveau local de l'hôte {host.LocalLevel:0.00}, " +
                    $"ami muet vu par l'hôte : {host.PeerMuted}, chemin {host.PathForTests}, durée {host.Duration}, " +
                    $"moteurs : {WebProcesses() - webBaseline} processus, {(WebMemory() - webMemoryBaseline) / 1048576} Mo privés (deux appels).");
                Log($"Périphériques de l'hôte : micros [{string.Join(" | ", host.Microphones)}], sorties [{string.Join(" | ", host.Speakers)}].");
                // Changement de micro pendant l'appel : la voix doit continuer d'arriver.
                if (host.Microphones.Count > 1)
                {
                    host.SelectedMicrophone = host.Microphones[^1];
                    var before = guest.BytesReceived;
                    await Settle(3000);
                    Log($"Micro changé pour « {host.SelectedMicrophone} » : l'invité a reçu {guest.BytesReceived - before} octets en 3 s. {host.Status}");
                    host.SelectedMicrophone = host.Microphones[0];
                }
                if (host.Speakers.Count > 1) host.SelectedSpeaker = host.Speakers[0];
                guest.Volume = 40;
            }));
            pages.Add(("c4-appel-raccroche", async () =>
            {
                guest?.HangUpCommand.Execute(null);
                await WaitUntil(() => host.Phase == CallPhase.Idle, 15_000);
                await Settle(1500);
                Log($"Raccroché par l'invité : hôte {host.Phase}, « {host.Status} ».");
                await Settle(3000);
                Log($"Après le raccroché : {WebProcesses() - webBaseline} processus du moteur restants.");
                guest?.Dispose();
            }));
            pages.Add(("c5-appel-code-inconnu", async () =>
            {
                guest = NewGuest();
                guest.JoinCode = "OG-K7P2Q";
                await guest.JoinCallCommand.ExecuteAsync(null);
                Log($"Code incomplet : « {guest.Status} »");
                host.JoinCode = "OG-ZZZZZZ";
                await host.JoinCallCommand.ExecuteAsync(null);
                await WaitUntil(() => host.Phase == CallPhase.Idle && host.HasStatus, 20_000);
                Log($"Code inconnu : « {host.Status} »");
                guest.Dispose();
            }));
            pages.Add(("c6-appel-code-expire", async () =>
            {
                host.JoinCode = "";
                await host.StartCallCommand.ExecuteAsync(null);
                await WaitUntil(() => host.Phase == CallPhase.Waiting, 20_000);
                await WaitUntil(() => host.Phase == CallPhase.Idle, 20_000);
                Log($"Code non saisi (8 s ici) : « {host.Status} »");
                relay?.Dispose();
            }));

            foreach (var (name, open) in pages.Where(p => Wanted(p.Name)))
            {
                await open();
                foreach (var size in sizes)
                {
                    window.Width = size.Width;
                    window.Height = size.Height;
                    await Settle(700);
                    var file = Path.Combine(output, $"{name}-{size.Width:0}x{size.Height:0}.png");
                    Save(window, file);
                    Log($"{Path.GetFileName(file)}");
                }
            }
            window.CloseForGame();

            foreach (var (name, create) in DialogSnapshots.All().Where(d => Wanted(d.Name)))
            {
                var dialog = create();
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = -32000;
                dialog.Top = -32000;
                dialog.ShowActivated = false;
                dialog.Show();
                await Settle(800);
                Save(dialog, Path.Combine(output, $"{name}.png"));
                Log($"{name}.png");
                dialog.Close();
            }

            if (Wanted("9-dock")) await SnapshotDock(services, output, Log);

            // « Changer le fond… » avec les vrais services (IGDB interrogé si les identifiants sont copiés) : rien n'est choisi.
            if (Wanted("7z-fond") && PickGame(services.GetRequiredService<ProfileStore>(), ValueAfter(args, GameArgument)) is { } backgroundGame)
            {
                var igdb = services.GetRequiredService<Platform.Artwork.IgdbClient>();
                var artwork = services.GetRequiredService<Platform.Artwork.ArtworkCache>();
                var steamHero = uint.TryParse(backgroundGame.SteamAppId, out var appId) ? Platform.Library.SteamOwnedLibrary.HeroPath(appId) : null;
                Func<Task<IReadOnlyList<Core.Artwork.IgdbBackground>>>? loadIgdb = igdb.IsConfigured
                    ? async () =>
                    {
                        var id = backgroundGame.IgdbGameId ?? Core.Artwork.Igdb.BestMatch(backgroundGame.Name, await igdb.SearchAsync(backgroundGame.Name))?.Id;
                        var found = id is { } igdbId ? await igdb.BackgroundsAsync(igdbId) : [];
                        Log($"Fonds IGDB de {backgroundGame.Name} (jeu {id}) : {found.Count}");
                        return found;
                    }
                    : null;
                var dialog = new Dialogs.BackgroundPickerDialog(backgroundGame.Name, steamHero, loadIgdb,
                    imageId => artwork.GetAsync(imageId, Core.Artwork.Igdb.BackgroundThumbSize), hasCustomBackground: true)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false,
                };
                dialog.Show();
                await Settle(8000);
                Save(dialog, Path.Combine(output, "7z-fond.png"));
                Log("7z-fond.png");
                dialog.Close();
            }

            // Galerie : mise en page hors fenêtre (sa hauteur dépasse l'écran), sur le fond de la fenêtre.
            if (!Wanted("0-galerie"))
            {
                Log("Terminé.");
                return;
            }
            var gallery = new ComponentGallery { Background = (Brush)Application.Current.FindResource("Brush.Window") };
            gallery.Measure(new Size(1000, double.PositiveInfinity));
            gallery.Arrange(new Rect(new Size(1000, gallery.DesiredSize.Height)));
            gallery.UpdateLayout();
            SaveElement(gallery, Path.Combine(output, "0-galerie.png"));
            Log("0-galerie.png");
            Log("Terminé.");
        }
        catch (Exception ex)
        {
            Log($"ÉCHEC : {ex}");
        }
    }

    /// <summary>
    /// Dock hors des écrans, toujours affiché (masquage automatique coupé pour la capture seulement : réglages en mémoire), sur le
    /// fond coloré : jeux épinglés des données de test, puis états d'exemple (lancement, désinstallé), puis à gauche.
    /// </summary>
    private static async Task SnapshotDock(IServiceProvider services, string output, Action<string> log)
    {
        var vm = services.GetRequiredService<DockViewModel>();
        var settings = services.GetRequiredService<Core.Settings.AppSettingsStore>().Get();
        (settings.DockEnabled, settings.DockAutoHide, settings.DockEdge) = (true, false, Core.Settings.DockEdge.Bottom);
        var dock = new Dock.DockWindow(vm, services.GetRequiredService<Platform.Display.FullscreenWatcher>()) { ShowActivated = false };
        // Fond façon fond d'écran, plus clair que le plateau : on voit ses bords et son opacité.
        var background = new LinearGradientBrush(Color.FromRgb(0x2B, 0x4C, 0x7E), Color.FromRgb(0x6B, 0x3F, 0x6E), 0);

        async Task Capture(string name)
        {
            (dock.Left, dock.Top) = (-32000, -32000);
            await Settle(1200);
            dock.UpdateLayout();
            var content = (FrameworkElement)dock.Content;
            var box = dock.Shelf.TransformToAncestor(content).TransformBounds(new Rect(dock.Shelf.RenderSize));
            box.Inflate(24, 24);
            box.Intersect(new Rect(content.RenderSize));
            Render(content, background, Path.Combine(output, $"{name}.png"), box);
            log($"{name}.png");
        }

        dock.ApplySettings(settings);
        (dock.Left, dock.Top) = (-32000, -32000);
        dock.Show();
        await Capture("9-dock");

        if (vm.Items.Count > 0) vm.Items[0].IsLaunching = true;
        if (vm.Items.Count > 1) vm.Items[1].InstallState = Core.Library.InstallState.Uninstalled;
        await Capture("9b-dock-etats");

        (settings.DockEdge, settings.DockIconShape, settings.DockIconSize) = (Core.Settings.DockEdge.Left, Core.Settings.DockIconShape.Cover, 48);
        dock.ApplySettings(settings);
        await Capture("9c-dock-gauche");
        dock.Close();
    }

    private static IReadOnlyList<Core.Diagnostics.DiagnosticResult> SampleDiagnostic()
    {
        static Core.Diagnostics.DiagnosticFix Fix(string id, string title, bool admin, bool reboot, bool advanced = false) => new(new Core.Changes.ReversibleChange
        {
            Id = id, Title = title, What = "", Why = "", RequiresAdmin = admin, RequiresReboot = reboot, Writes = [],
        }, advanced);
        return
        [
            new() { CheckId = "demo.gamemode", Title = "Mode Jeu", Status = Core.Diagnostics.DiagnosticStatus.NeedsAttention,
                Summary = "Désactivé : Windows ne donne pas la priorité au jeu.",
                Explanation = "Le Mode Jeu suspend les mises à jour et les notifications pendant la partie.",
                Details = [@"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled = 0"],
                Fixes = [Fix("fix.demo.gamemode", "Activer le Mode Jeu", admin: false, reboot: false)] },
            new() { CheckId = "demo.hvci", Title = "Intégrité de la mémoire", Status = Core.Diagnostics.DiagnosticStatus.NeedsAttention,
                Summary = "Activée : quelques pour cent de performances en moins dans certains jeux.",
                Explanation = "Elle protège le noyau de Windows. La désactiver est un compromis de sécurité.",
                Fixes = [Fix("fix.demo.hvci", "Désactiver l'intégrité de la mémoire", admin: true, reboot: true, advanced: true)] },
            new() { CheckId = "demo.vbs", Title = "Sécurité basée sur la virtualisation", Status = Core.Diagnostics.DiagnosticStatus.Error,
                Summary = "", Explanation = "Accès refusé (0x80041003)." },
            new() { CheckId = "demo.hags", Title = "Planification GPU à accélération matérielle", Status = Core.Diagnostics.DiagnosticStatus.Info,
                Summary = "Désactivée. Gain variable selon les jeux.",
                Fixes = [Fix("fix.demo.hags", "Activer la planification GPU", admin: true, reboot: true)] },
            new() { CheckId = "demo.refresh", Title = "Fréquence de l'écran", Status = Core.Diagnostics.DiagnosticStatus.Ok, Summary = "165 Hz, la fréquence maximale." },
        ];
    }

    private static GameProfile? PickGame(ProfileStore store, string? name)
    {
        var profiles = store.GetAll();
        return name is { Length: > 0 }
            ? profiles.FirstOrDefault(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            : profiles.Where(p => p.SteamAppId is not null && File.Exists(p.ExePath)).OrderBy(p => p.Name).FirstOrDefault()
              ?? profiles.FirstOrDefault();
    }

    /// <summary>Contenu de la fenêtre (sans le cadre Windows) sur son fond, à 96 ppp : 1 pixel = 1 unité WPF.</summary>
    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        Render((FrameworkElement)window.Content, window.Background, path);
    }

    private static void SaveElement(System.Windows.Controls.Control element, string path) => Render(element, element.Background, path);

    /// <summary>Rendu de root (ou seulement de region, dans son repère) sur un fond.</summary>
    private static void Render(FrameworkElement root, Brush background, string path, Rect? region = null)
    {
        var box = region ?? new Rect(new Size(root.ActualWidth, root.ActualHeight));
        var size = box.Size;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background, null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = box },
                null, new Rect(size));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Premier élément de ce type et de ce nom dans l'arbre visuel.</summary>
    private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.Name == name) return match;
            if (FindByName<T>(child, name) is { } found) return found;
        }
        return null;
    }

    /// <summary>Laisse la mise en page, les liaisons et les lectures asynchrones se faire.</summary>
    private static async Task Settle(int milliseconds)
    {
        await Task.Delay(milliseconds);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }
        await Settle(500);
    }

    private static string? ValueAfter(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>« 1240x860,880x600 » → tailles en unités WPF.</summary>
    private static Size[]? ParseSizes(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Split('x'))
            .Select(p => new Size(double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
}
