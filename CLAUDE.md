# OptiGame

Application Windows qui optimise le PC pendant les sessions de jeu (plus de FPS, moins de drops) :
diagnostic, profils de jeu appliqués/restaurés automatiquement, mesure avant/après via PresentMon.

Stack : C# / .NET 10 (LTS), WPF en MVVM (CommunityToolkit.Mvvm), icône de notification (H.NotifyIcon.Wpf),
DI Microsoft.Extensions.DependencyInjection, tests xUnit.

## Principes non négociables

1. **Tout réglage modifié est réversible.** Avant chaque changement, l'état d'origine est sauvegardé en JSON
   dans `%LocalAppData%\OptiGame\` (write-ahead : capture → persistance atomique → application).
   - Changements de session (`session.json`) : restaurés à la fermeture du jeu, et au démarrage suivant si
     l'appli a planté pendant une session.
   - Corrections du diagnostic (`fixes.json`) : permanentes mais annulables depuis l'UI.
   - SEULE exception, validée par l'utilisateur : l'installation de pilotes (page « Pilotes »). Non journalisable ;
     annoncée dans une confirmation dédiée (`DriverInstallDialog`, case « je comprends » obligatoire) avec le chemin du
     retour en arrière (Gestionnaire de périphériques → « Restaurer le pilote ») et un point de restauration si possible.
2. **Rien n'est appliqué sans action explicite de l'utilisateur.** Chaque correction affiche ce qu'elle
   change, pourquoi, et si elle nécessite admin / redémarrage.
3. **Pas de tweaks placebo ou risqués.** Interdits : tweaks registre réseau/système (NetworkThrottlingIndex,
   SystemResponsiveness…), flags plein écran `GameDVR_FSE*`, vidage de RAM en boucle, désactivation de
   Defender / Windows Update / services système, timer resolution.
4. **Léger en tâche de fond.** Pas de polling agressif : événements WMI (`Win32_ProcessStartTrace` /
   `Win32_ProcessStopTrace`) pour détecter les jeux, lectures à la demande pour le diagnostic.
5. **Élévation isolée.** Manifeste `requireAdministrator` en v1, mais toutes les opérations nécessitant admin
   passent par `OptiGame.Platform/Privileged/` (`IPrivilegedOperations`) pour pouvoir être extraites plus tard.

## Architecture

```
src/OptiGame.Core/       net10.0, aucune dépendance Windows, 100 % testable
  State/                 SettingValue (Absent | DWord | QWord | String…), SettingTarget (Kind, Path, Name),
                         ISettingAccessor (lecture/écriture d'une famille de réglages), JsonStateStore
                         (écriture atomique), ChangeJournal (write-ahead, Apply / Undo / RestoreAll)
  Changes/               ReversibleChange : description par DONNÉES (liste de SettingWrite) + What / Why /
                         RequiresAdmin / RequiresReboot. Pas de code de restauration par changement : le journal
                         restaure via l'accesseur du Kind, ce qui fonctionne aussi après un crash.
  Diagnostics/           IDiagnosticCheck → DiagnosticResult (OK / ÀCorriger / Info), un fichier par contrôle
  Abstractions/          IRegistry, IWmi, IPowerPlans, IDisplayInfo, IPowerStatus, IGpuSchedulingInfo…
  Profiles/              GameProfile, ProfileStore (profiles.json), ProfileValidator (processus protégés)
  Sessions/              SessionPlan (profil → changements + texte), GameSessionManager (application, restauration,
                         reprise après crash ; une session à la fois ; IProcessControl pour fermer/prioriser)
  Library/               Vdf (format KeyValues de Steam), ExeRanking (choix de l'exe principal, validé sur cas réels)
  Measurement/           PresentMonCsv (colonnes lues par NOM, v1/v2, « NA », chaîne d'affichage principale),
                         FrameStats (FPS moyens pondérés par le temps, 1 %/0,1 % low = moyenne des pires images,
                         P99), CaptureRequest (arguments PresentMon 2.x vérifiés via --help), CaptureStore
  Settings/              AppSettings (settings.json : dossiers de jeux, chemin de PresentMon, identifiants IGDB)
  Artwork/               Igdb (requête Apicalypse, lecture des réponses, meilleur résultat, URL d'images)
  Launching/             LaunchPlanner (Automatique / Steam / Exécutable / Lanceur)
  Playtime/              PlaytimeStore (playtime.json, session écrite « ouverte » dès le début de partie ; fin inconnue
                         après crash = Incomplete, jamais de durée inventée), PlaytimeTracker (branché AVANT Recover)
src/OptiGame.Platform/   net10.0-windows : implémentations réelles (registre, WMI, P/Invoke), Privileged/,
                         Processes/, Startup/ (tâche planifiée), Measurement/ (runner PresentMon)
src/OptiGame.App/        WPF : composition DI, tray, vues/viewmodels, dialogue de confirmation
  Themes/Theme.xaml      thème sombre « gaming » : palette (Brush.*), styles Button.Primary/Secondary/Ghost/Danger,
                         Card, Banner, Badge, NavButton, Text.* — jamais de couleur codée en dur dans les vues
  App.xaml               ThemeMode="Dark" (Fluent .NET 10) + accent vert : redéfinir les clés
                         SystemColors.AccentColor…Key (les clés nommées Accent*Brush seules ne suffisent pas)
  Navigation             barre latérale (MainViewModel.NavItems) ; page = ViewModel, vue choisie par DataTemplate
                         implicite (jamais ContentTemplate explicite : il s'applique même quand le contenu est null)
  Mes jeux               LibraryViewModel (grille de jaquettes) → GamePageViewModel (bannière + éditeur du profil)
  Dock/                  DockWindow (transparente, Topmost, WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, zones alpha 0 =
                         clics traversants) + DockController (crée/ferme selon settings, masque pendant une session
                         ou une appli plein écran via Platform/Display/FullscreenWatcher = EVENT_SYSTEM_FOREGROUND +
                         SHQueryUserNotificationState). Animations uniquement pendant les transitions : grossissement
                         = étirement continu de l'axe autour de la souris (Core/Dock/DockMagnification.Place), appliqué
                         en transformations de RENDU (jamais LayoutTransform : la rangée se recentrerait et le dock
                         sursauterait), intensité animée par ressort (DockSpring, boucle CompositionTarget.Rendering
                         arrêtée une fois au repos), glisser-déposer interne (capture souris + Core/Dock/DockReorder, jamais DragDrop OLE),
                         onde au clic. Ordre du dock = GameProfile.DockOrder, modifié seulement par ProfileStore.SetPinned
                         / MoveInDock (DockViewModel.Move déplace d'abord l'élément affiché, pour animer son arrivée).
                         Capture de test : PrintWindow sur la fenêtre « OptiGame — Dock » (zones transparentes = noir).
tests/OptiGame.Core.Tests/      xUnit + fakes en mémoire (journal, logique des contrôles)
tests/OptiGame.Platform.Tests/  intégration sur le vrai registre, UNIQUEMENT sous HKCU\Software\OptiGame.Tests
tools/OptiGame.DiagDump/        diagnostic en console (lecture seule), pour vérifier les lectures système
```

Données fictives pour tester l'UI : `$env:OPTIGAME_DATA_DIR='<dossier temporaire>'` redirige tout le dossier de
données ; `DiagDump -- --import-capture <csv> <libellé> <date ISO>` y ajoute une capture (refusé sans la variable).
`DiagDump -- --games [dossier…]` affiche la recherche des jeux installés.

IGDB (jaquettes) : identifiants Twitch saisis dans Paramètres, secret chiffré DPAPI (`Platform/Artwork/SecretProtector`),
jeton gardé en mémoire, 4 requêtes/s max, images en cache dans `covers\`. Seul le nom du jeu est envoyé. Les jaquettes
d'un profil ne changent que par `ProfileStore.SetArtwork` (`Save` conserve celles déjà enregistrées). Format de réponse
confirmé sur de vrais appels le 2026-09-30 (5 jeux trouvés, jaquettes et bannières).

PresentMon : version console téléchargée depuis github.com/GameTechDev/PresentMon (v2.6.0, signée Intel) dans
`%LocalAppData%\OptiGame\tools`, détectée automatiquement ; capture = ETW, droits admin requis.

Tests manuels de l'UI sans UAC : `$env:__COMPAT_LAYER='RunAsInvoker'` avant de lancer l'exe (l'appli démarre
non élevée ; les lectures fonctionnent, les écritures HKLM échoueront proprement).

Sessions de jeu (Platform/Processes) :
- Détection : `Win32_ProcessStartTrace` (admin requis) + comparaison sur le chemin complet de l'exe ; fin de
  partie via `Process.Exited`. Aucun polling.
- Fermeture : fenêtre principale fermée normalement, arrêt forcé après 5 s. Jamais les exe de `%windir%`, les
  services (autre session) ni les noms de `ProfileValidator.ProtectedProcesses`.
- Réglage `process` : valeur = [chemin, ligne de commande] ; journalisé seulement si le programme tournait.
  Relance via le jeton de l'Explorateur (`UnelevatedLauncher`) : jamais de relance élevée. Une relance ratée est
  signalée puis retirée du journal (`ChangeJournal.Discard`) ; les réglages système ratés restent en attente.
- Démarrage auto : tâche planifiée importée en XML (`Startup/AutoStartService`), argument `--minimized`.
- Lancement (« Jouer ») : `Core/Launching/LaunchPlanner` (Automatique / Steam / Exécutable / Lanceur) +
  `Platform/Processes/GameLauncher`, toujours via `UnelevatedLauncher`. Steam = `steam.exe -applaunch <appid>`
  (SteamExe dans HKCU\Software\Valve\Steam), appid retrouvé dans les manifestes si absent du profil.
  `DiagDump -- --launch-plan` montre ce qui serait lancé pour chaque profil, sans rien lancer.
- « Page Steam » (page du jeu) : `Core/Launching/SteamStorePage`, `"steam.exe" -- "steam://store/<appid>"` via
  `UnelevatedLauncher` (même forme que HKCR\steam\shell\open\command), navigateur si Steam est absent.

Règle de dépendance : `App → Platform → Core`. La logique de décision (statut d'un check, restauration)
vit dans Core ; Platform ne fait que lire/écrire le système.

## Faits vérifiés sur la machine de dev (Windows 11 25H2, build 26200, AtlasOS)

- `HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled` : peut être **absente** → absent = Mode Jeu activé.
- `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode` : peut être **absente** → lire l'état réel
  de HAGS via `D3DKMTQueryAdapterInfo(KMTQAITYPE_WDDM_2_7_CAPS)` ; registre 2 = ON, 1 = OFF (redémarrage requis).
- Enregistrement en arrière-plan : `HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled`
  (+ `AppCaptureEnabled`) ; interrupteur global : `HKCU\System\GameConfigStore\GameDVR_Enabled`.
- `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` : valeurs `Clé=Valeur;` par chemin d'exe → **fusionner**
  `GpuPreference=N;` avec les paires existantes (ex. `AutoHDREnable`). Windows y écrit de lui-même `AutoHDREnable=2097;`
  et parfois `AppStatus=…`, et des noms avec `.\` (« Scrap Mechanic\.\Release\… ») : comparer les chemins normalisés.
  Auto HDR par jeu = encodage NON documenté et non élucidé (expérience du 2026-09-30 dans Paramètres > Graphiques : 2097 par
  défaut, 6193 après désactivation PUIS réactivation — même valeur dans les deux états, aucune autre clé modifiée ; Windows
  ajoute aussi `SwapEffectUpgradeEnable=1`) → OptiGame n'écrit JAMAIS AutoHDREnable, il l'affiche et ouvre
  `ms-settings:display-advancedgraphics`. Seule GpuPreference (0/1/2, documentée) est écrite, et seulement sur les PC à
  plusieurs cartes. Carte « Graphismes (Windows) » : `Core/Settings/GameGraphics` + `App/Services/GameGraphicsService`,
  journal fixes.json (annulable), état relu, jamais stocké.
- HDR des écrans : `DisplayConfigGetDeviceInfo(GET_ADVANCED_COLOR_INFO)`, bit 2 = couleur étendue SDR imposée, PAS du HDR
  (BenQ = 0x5, Samsung en HDR = 0x3). En PowerShell, le passage des structures échoue (code 31) : tester en C#.
- VBS/HVCI : WMI `root\Microsoft\Windows\DeviceGuard` / `Win32_DeviceGuard` ; désactivation avancée via
  `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled`.
- `powercfg` a une sortie localisée → ne jamais la parser ; utiliser `powrprof.dll`. Les plans personnalisés
  (ex. « Atlas Power Scheme ») sont un statut Info.
- Une valeur de registre absente se sauvegarde comme `Absent` et se restaure par **suppression**.
- CSV réel de PresentMon 2.6.0 (`--v2_metrics`) : colonnes `FrameTime`, `CPUBusy`, `GPUTime`, `DisplayedTime`… SANS
  préfixe `Ms`, contrairement à sa documentation. Toujours valider un format sur un vrai fichier, et ne jamais
  supprimer un fichier qu'on n'a pas réussi à lire.
- Pilotes (page « Pilotes », `Core/Drivers` + `Platform/Drivers`, lecture seule ; `DiagDump -- --drivers`) :
  - NVIDIA, services NON documentés vérifiés le 2026-09-30 (vraies réponses dans `tests/…/Drivers/Samples`) :
    liste des produits `www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3` (XML, nom EXACT : « GeForce RTX
    3070 » ≠ « … Ti » ≠ « … Laptop GPU » ; RTX 3070 = psid 120, pfid 933) puis `gfwsl.geforce.com/…/AjaxDriverService.php
    ?func=DriverManualLookup` (JSON servi en text/html ; languageCode=1033 → date « Tue Sep 22, 2026 » ; osID 57 = Windows 11).
  - Version Windows → NVIDIA : 5 derniers chiffres des deux derniers nombres (32.0.15.9186 → 591.86).
  - Téléchargement autorisé seulement depuis `https://*.download.nvidia.com` (redirections comprises), dans
    `%LocalAppData%\OptiGame\downloads`. Avant ouverture (et encore juste avant, dans `IPrivilegedOperations`) :
    WinVerifyTrust + signataire « CN=NVIDIA Corporation, O=NVIDIA Corporation » (lu sur l'installeur 617.14 par requêtes
    partielles : le serveur accepte les Range). Installeur refusé → renommé `.non-verifie`, jamais ouvert.
  - Chipset AMD (`Core/Drivers/AmdChipset`, `Platform/Drivers/AmdChipsetClient`) : pas de service, la page
    `www.amd.com/en/support/downloads/drivers.html/chipsets/<am4|am5>/<chipset>.html` est lue (blocs `<article …
    driver-download-details>` ; extrait réel B450 dans les tests). Chipset déduit du nom de carte mère (Win32_BaseBoard).
    Paquet « AMD Chipset Software » absent de la machine de dev (pilotes séparés) → comparaison par dates. Téléchargement
    depuis `https://drivers.amd.com` UNIQUEMENT avec `Referer: https://www.amd.com/` (sinon redirection vers une page HTML
    « Download-Incomplete ») ; signataire « CN=Advanced Micro Devices, O=Advanced Micro Devices » (Sectigo). Chaîne complète
    vérifiée sur le vrai installeur 8.08.12.551 (81 537 472 octets), sans l'exécuter. Règles par fabricant :
    `OfficialInstallers` ; installeurs rangés dans `downloads\<fabricant>\`.
  - Protection du système : `HKLM\…\SPP\Clients` n'est lisible QU'EN ADMIN (accès refusé sinon) → état « Unknown » hors
    admin ; l'état réel est journalisé à chaque installation. Création de point : WMI `root\default:SystemRestore`.
  - Windows Update : COM `Microsoft.Update.Session`, « IsInstalled=0 and Type='Driver' and IsHidden=0 », ≈ 25 s, fonctionne
    sous AtlasOS même sans droits admin. Le pilote NVIDIA de Windows Update est masqué si celui de NVIDIA est plus récent.
- Nouveaux jeux Steam (`Core/Library/NewSteamGames`, `Platform/Library/SteamLibraryWatcher`, bandeau de « Mes jeux ») :
  FileSystemWatcher sur `steamapps\appmanifest_*.acf` de chaque bibliothèque, 3 s après la dernière écriture ; proposé si
  `StateFlags` a le bit 4 (entièrement installé ; 1026 = téléchargement en cours), sans profil et absent de
  `settings.SteamKnownAppIds` (null = premier passage : l'existant est mémorisé, pas proposé). Ajouté ou ignoré = mémorisé.
- Note des jeux (`Core/Rating`, `App/Services/GameRatingService`, pastille des jaquettes + carte « Note sur ce PC ») :
  - Estimation : configuration requise de Steam (`store.steampowered.com/api/appdetails?appids=<id>&filters=basic&l=english`,
    `pc_requirements.minimum/recommended` en HTML, lignes « Graphics: » et « Memory: » ; 4 vraies réponses dans les tests),
    gardée 30 jours dans `requirements.json`. Cartes comparées par `GpuPerformance` (indices APPROXIMATIFS, GTX 1060 = 100,
    affichés comme estimation), résolution ramenée au 1080p (pixels^0,7). Recommandé atteint = « Élevé ».
  - Mesure : `Platform/Measurement/AutoCapture` = 1 capture PresentMon de 60 s après 4 min de partie, session ETW
    `OptiGame_AutoCapture` (jamais celle de la page Mesures), 5 dernières gardées par jeu, désactivable (Paramètres).
    Note = fluidité (60 % FPS moyens + 40 % 1 % low) / min(fréquence de l'écran, 120 Hz), médiane des 5 dernières captures.
  - Réglages du jeu ILLISIBLES par OptiGame : l'utilisateur indique le sien (`GameProfile.GraphicsPreset`, modifié seulement
    par `ProfileStore.SetGraphicsPreset`), copié dans chaque capture ; non indiqué = conseil relatif, sans réglage conseillé.
  - Limitation (`GameRatings.Classify`) d'après `FrameLoad` (Σ GPUBusy / Σ FrameTime, Σ CPUWait / Σ FrameTime, colonnes
    vérifiées sur de vrais CSV 2.6.0) : GPU ≥ 85 % = carte graphique ; sinon attente ≥ 15 % ou FPS ≈ fréquence = plafond ;
    sinon processeur. Réel : Overwatch plafonné 76 %/43 %, en V-Sync 82 %/64 % ; Void Crew 98 %/2 %. Captures antérieures :
    charge recalculée une fois depuis le CSV (`GameRatingService`, `CaptureStore.SetLoad`).
- Temps de jeu Steam : `<SteamPath>\userdata\<accountid>\config\localconfig.vdf`, UserLocalConfigStore > Software > Valve >
  Steam > apps > <appid> > `Playtime` (MINUTES) + `LastPlayed` (secondes Unix), réécrit par Steam à la fin d'une partie.
  Compte : `HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser` (0 si Steam est fermé), sinon `config\loginusers.vdf` :
  le Steam actuel n'écrit PLUS `MostRecent`, seulement `Timestamp` (accountid = SteamID64 − 76561197960265728).
  `DiagDump -- --steam-playtime` affiche ce qui est lu pour chaque profil.
- Pile des fenêtres : le bureau (`Progman`) est tout en bas. Une fenêtre « collée au bureau » (dock sans masquage auto)
  doit se placer juste AU-DESSUS de lui (WM_WINDOWPOSCHANGING, cf. `DockWindow.LowestWindowAboveDesktop`) : avec
  HWND_BOTTOM, elle passerait sous le bureau, invisible. RocketDock fait de même. « Afficher le bureau » (Win+D) fait
  passer le bureau devant : tant qu'aucune application ne le recouvre (`Core/Dock/DesktopRules`, fenêtres « cloaked »
  ignorées : 9 sur la machine de dev), le dock passe au premier plan (méthode de Rainmeter), puis se recolle.
- WPF gèle les Freezable (ScaleTransform, brushes…) déclarés dans un DataTemplate : pour les animer, donner à
  chaque élément sa propre instance (cf. `DockWindow.SetScale`). Toute erreur d'interface passe par
  `DispatcherUnhandledException` (journalisée, l'appli continue).
- `WqlEventQuery` n'accepte que `SELECT * FROM …` : une liste de propriétés lève « Paramètre non valide ».
- Les notifications Windows peuvent être masquées (AtlasOS) : toute erreur importante doit aussi apparaître
  dans la fenêtre et dans `%LocalAppData%\OptiGame\logs\optigame.log` (UTF-8 : `Get-Content -Encoding UTF8`).

## Conventions

- UI et messages utilisateur en français ; code (identifiants) en anglais.
- Nullable activé, avertissements traités comme erreurs.
- Un commit par étape fonctionnelle ; ne passer à la phase suivante qu'après test par l'utilisateur.
- Vérifier tout chemin de registre / classe WMI sur la machine avant de s'en servir ; signaler s'il est absent.

## Commandes

```powershell
dotnet build OptiGame.slnx
dotnet test OptiGame.slnx
# Compiler et relancer l'appli (ferme proprement l'instance en cours via quit.request, qui verrouillerait les DLL ;
# restaure une éventuelle session de jeu). `dotnet run` échoue (erreur 740) car il ne peut pas déclencher l'UAC.
.\scripts\dev-run.ps1
dotnet run --project tools/OptiGame.DiagDump   # diagnostic lecture seule en console, sans élévation
```
