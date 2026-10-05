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
                         ; OneClickOptimization = en-tête de la page Diagnostic (UNE page : verdict DiagnosticVerdict, jamais
                         « prêt » si un contrôle est Non vérifié ; bouton unique « Appliquer les N optimisations… » = optimisations
                         des contrôles « À corriger », jamais avancées ni facultatives ; « Tout restaurer… » ne restaure que les ids
                         « fix. », jamais « game. ») ; contrôles par statut À corriger / Non vérifié (DiagnosticStatus.Error) /
                         À savoir / OK, chacun restaurable dans « Optimisations actives »
  Abstractions/          IRegistry, IWmi, IPowerPlans, IDisplayInfo, IPowerStatus, IGpuSchedulingInfo…
  Profiles/              GameProfile, ProfileStore (profiles.json), ProfileValidator (processus protégés), GameChanges
                         (réglages « game.* » faits pour un jeu dans fixes.json : gardés quand on le retire, cités à la confirmation)
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
  Themes/Theme.xaml      système de design (docs/design-system.md, refonte UI depuis le 2026-10-05) : couleurs à rôle (marque/action/
                         sélection = vert ; statuts Fluent Ok/Warning/Danger/Info + Soft ; séries de graphe), échelle typographique
                         FontSize.*/Text.*, espacements Space.*/Padding.*/Margin.*, rayons Radius.*, Focus.Ring, boutons Button.*,
                         Badge.*, Segment, Expander.Row, NavButton ; Controls/InfoBar et Controls/IconButton (nom UIA = libellé).
                         Dialogues : AUCUNE MessageBox (sauf fichier d'état illisible, avant DI) ; Dialogs/DialogWindow +
                         Controls/DialogLayout ; IDialogService.Confirm(titre, message, VERBE, isDestructive) / ShowInfo /
                         ShowError(titre, message, détails techniques) — « Annuler » par défaut ; Core/Text/FrenchText (pluriels,
                         espaces insécables, SEUL format de date : Date « 22 sept. 2026 » / DateAndTime / When « aujourd'hui à 14:32 »).
                         JAMAIS de couleur, taille de police ni rayon codé en dur dans les vues ; vérifier avec ui-snapshots.ps1
  Assets/                Logo (choisi le 2026-10-04 : cadran de vitesse ouvert en « O » + triangle « lecture ») : OptiGame.svg
                         (case 64) et OptiGame-small.svg (variante épaisse pour ≤ 24 px) → OptiGame.ico = 16, 20, 24 depuis
                         -small, 32 → 256 depuis le logo (rsvg-convert, puis `convert i16.png … i256.png OptiGame.ico` d'ImageMagick,
                         SANS -define icon:auto-resize : il recalcule toutes les tailles depuis une seule image). Exe :
                         ApplicationIcon (toutes les fenêtres le reprennent) ; zone de notification : App.LoadTrayIcon, taille exacte
                         de Platform/Display/IconMetrics (20 px à 125 %) ; barre latérale et dock : DrawingImage Logo.Image /
                         Logo.Mark de Theme.xaml (vectoriels, brosses du thème). Le nom reste « OptiGame ».
  App.xaml               ThemeMode="Dark" (Fluent .NET 10) + accent vert : redéfinir les clés
                         SystemColors.AccentColor…Key (les clés nommées Accent*Brush seules ne suffisent pas)
  Navigation             barre latérale (MainViewModel.NavItems + SettingsItem en pied, pastilles, compacte < 1008) ; page =
                         ViewModel, vue choisie par DataTemplate implicite (jamais ContentTemplate explicite : il s'applique même
                         quand le contenu est null). Coque (docs/design-system.md) : Services/ShellAlerts = alertes persistantes
                         en InfoBar (erreurs de détection / restauration : JAMAIS seulement en notification), raccourcis Ctrl+1…5,
                         Ctrl+F, F5, Échap/Alt+←, place de la fenêtre dans settings.json (Core/Settings/WindowLayout),
                         UnsavedChangesGuard pour Quitter
  Mes jeux               LibraryViewModel (grille de jaquettes Controls/CoverTile, installés et non installés ; « Ajouter des jeux ▾ »,
                         recherche / tri / filtres en WrapPanel, « 3 sur 42 jeux », état « aucun résultat », messages en InfoBar,
                         nouveaux jeux Steam en UN bandeau) → GamePageViewModel (fiche : fil d'Ariane, bannière à hauteur de
                         contenu avec Jouer / « Arrêter l'optimisation… » + « … », 3 onglets GameTab Vue d'ensemble / Optimisation /
                         Propriétés, dernier onglet gardé ; réglages de partie + propriétés = barre « Enregistrer » (Ctrl+S,
                         IsDirty COMPARÉ à la version enregistrée, point sur l'onglet modifié) ; réglages permanents NVIDIA /
                         Windows = « Appliquer… » confirmé + « Restaurer l'original… » ; images décodées par ImageLoader, IsAsync).
                         Rapidité : jaquettes décodées UNE fois par Converters/ImageLoader (cache borné à 96 Mo, images
                         figées) à la taille réelle à l'écran (198 unités × échelle d'affichage de MainWindow, PerMonitorV2),
                         grises = pixels COPIÉS dans une image autonome (un FormatConvertedBitmap garde l'image couleur :
                         733 Ko par jaquette mesurés au lieu de 214) ; fenêtre masquée (fermer = masquer, appli en zone de
                         notification) ou section décochée → jeux non installés ramenés à la 1re page, cache vidé, un GC
                         (mesuré : ≈ 1,2 Mo par carte affichée avant, ≈ 160 Ko après ; 631 jeux affichés = 937 Mo avant,
                         325 Mo après ; démarrage 332 → 202 Mo). Page suivante chargée SEULEMENT si l'on descend ou si le
                         contenu / la fenêtre grandit, jamais fenêtre masquée (WPF met en page une fenêtre masquée : une liste
                         raccourcie, défilement resté en bas, rechargeait les 631) ; filtre changé → 1re page, défilement en
                         haut ; relecture des bibliothèques → même longueur (genres reconstruits sans rafraîchir) ; grilles liées à CoverImage avec IsAsync=True (décodage hors du thread UI) ; jeux non
                         installés affichés par pages de 48 (VisibleUninstalled, page suivante près du bas : pas de WrapPanel
                         virtualisé en WPF) ; recherche appliquée après 200 ms sans frappe ; caches des lanceurs (appinfo.vdf,
                         catalogue Epic, base Galaxy) relus seulement si leur date/taille change (Platform/Library/FileStamps).
                         Journal : « Fenêtre affichée … ms », « Bibliothèques des magasins lues en … ms », chacune avec la
                         mémoire (Core/Logging/MemoryUsage : RAM, privée, tas .NET alloué / réservé, gros objets, hors .NET).
                         Mesure à la demande : créer `memory.request` dans le dossier de données (Platform/Startup/
                         RequestFileWatcher) → journal « Mémoire (demandée) », détail des cartes et jaquettes, puis « après
                         nettoyage complet » (GC forcé). `DiagDump -- --memory` : coût de chaque lecture lourde seule et des
                         jaquettes (ImageLoader de l'appli lié à l'outil, d'où UseWPF dans DiagDump).
  Pendant une partie     Services/InGameFootprint (réglage `LightDuringGames`, activé par défaut) : fenêtre principale FERMÉE
                         (App.CloseMainWindowForGame ; MainWindow TRANSITOIRE, recréée à la demande, même page et même place,
                         rouverte à la fin si elle était affichée, réduite si elle l'était ; gardée si une boîte de dialogue
                         est ouverte) et dock FERMÉ (DockController, surveillance du plein écran arrêtée ; recréé par Apply à la
                         fin — UpdateSuppression ne crée jamais la fenêtre : fullscreen.Start() peut déclencher FullscreenChanged
                         avant que _window soit posé). Toujours : Services/MemoryRelief (1re page des non installés, cache
                         d'images vidé, UN GC regroupé, mémoire écrite au journal « Partie en cours : … ») et Services/
                         GameTimeGate (relecture des bibliothèques, jaquettes Epic/GOG et IGDB, notes, nouveaux jeux Steam, temps
                         Steam reportés à la fin de la partie, une fois par sorte). Vues : abonnement aux événements des
                         ViewModels sur Loaded / Unloaded (jamais DataContextChanged : la vue jetée resterait abonnée et
                         garderait toute la fenêtre) ; DockWindow.OnClosed se désabonne de CompositionTarget.Rendering (statique).
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
                         Replacé SEULEMENT si un réglage du dock change (Core/Dock/DockAppearance : settings.json est réécrit
                         pour la place de la fenêtre, etc.), puis aperçu de 2 s ; replacé aussi sur WM_DISPLAYCHANGE / DPICHANGED /
                         SETTINGCHANGE(SPI_SETWORKAREA). Jamais Application.MainWindow (propriétaire des dialogues). Bande du bord
                         quittée sans toucher le dock = il se range. Animations coupées (réglage « Animations », selon Windows par défaut ; AtlasOS les coupe) : ni glissement, ni
                         ressort, ni onde. Jaquette : « Lancement… » (12 s ou début de partie, clics ignorés), grisée si désinstallé
                         / disque absent (GameInstallation, hors thread UI). Clic droit jeu : Jouer, fiche, Déplacer ←/→, Retirer ;
                         clic droit plateau : Ouvrir OptiGame, Masquer automatiquement, Paramètres du dock…, Désactiver… Ordre au
                         clavier : Paramètres › Dock › « Jeux du dock » (Monter / Descendre / Retirer, focus rendu à la ligne).
                         Captures : ui-snapshots -Only 9 (étagère seule, fond coloré) ; sinon PrintWindow sur « OptiGame — Dock ».
tests/OptiGame.Core.Tests/      xUnit + fakes en mémoire (journal, logique des contrôles)
tests/OptiGame.Platform.Tests/  intégration sur le vrai registre, UNIQUEMENT sous HKCU\Software\OptiGame.Tests
tools/OptiGame.DiagDump/        diagnostic en console (lecture seule), pour vérifier les lectures système
```

Pages Mesures et Paramètres (refonte, étape 7) : mesures automatiques ajoutées à la liste dès AutoCapture.CaptureAdded ;
PresentMon se règle dans Paramètres › Mesures (MeasuresViewModel.ChoosePresentMon) ; Paramètres en Controls/SettingRow.

Données fictives pour tester l'UI : `$env:OPTIGAME_DATA_DIR='<dossier temporaire>'` redirige tout le dossier de
données ; `DiagDump -- --import-capture <csv> <libellé> <date ISO>` y ajoute une capture (refusé sans la variable).
`DiagDump -- --games [dossier…]` affiche la recherche des jeux installés.

IGDB (jaquettes) : identifiants Twitch saisis dans Paramètres, secret chiffré DPAPI (`Platform/Artwork/SecretProtector`),
jeton gardé en mémoire, 4 requêtes/s max, images en cache dans `covers\`. Seul le nom du jeu est envoyé. Les jaquettes
d'un profil ne changent que par `ProfileStore.SetArtwork` (`Save` conserve celles déjà enregistrées). Format de réponse
confirmé sur de vrais appels le 2026-09-30 (5 jeux trouvés, jaquettes et bannières).
Fond de la fiche : `GameProfile.CustomHeroFile` (choisi par « Changer le fond… », `BackgroundPickerDialog` ; copie dans
`covers\heroes`, nom neuf à chaque choix, ancien fichier supprimé ; ne change que par `ProfileStore.SetCustomHero`), sinon
`library_hero.jpg` de Steam (`SteamOwnedLibrary.HeroPath`, local), sinon 1re illustration IGDB (parfois un logo). Fonds IGDB :
`fields artworks.image_id,screenshots.image_id; where id = <jeu>` (vérifié en vrai le 2026-10-05 : Portal 2 = 8 images).

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
- Boost du processeur (`Diagnostics/Checks/CpuBoostCheck`) : réglages du plan ACTIF sur secteur, `PERFBOOSTMODE`
  (0 = désactivé ; défaut Windows = 2 « Offensif », relevé dans Utilisation normale et Haute performance) et `PROCTHROTTLEMAX`
  (< 100 % coupe le turbo). Cible `KnownSettings.PowerSetting` (kind `power-setting`, Path = « plan/sous-groupe/réglage ») :
  lecture `PowerReadACValueIndex` (code 2 = réglage absent du plan), écriture `IPrivilegedOperations.WriteAcPowerSetting`
  (`PowerWriteACValueIndex` puis réactivation du plan s'il est actif). Écriture vérifiée sur une copie temporaire d'un plan.
  Machine de dev : Atlas = Offensif, 100 % → OK. Écartés comme placebo : affinité / « tous les cœurs », RAM virtuelle pour
  les FPS, arguments de lancement génériques (-USEALLAVAILABLECORES, -high = priorité déjà gérée, -malloc=system).
- Disque du jeu (carte « Disque du jeu » de la page du jeu ; `Core/Library/GameDisk` + `Platform/Storage/GameDiskReader`,
  `DiagDump -- --disks`) : lettre → `MSFT_Partition.DiskNumber` = `MSFT_PhysicalDisk.DeviceId` (root\Microsoft\Windows\Storage,
  sans admin). `MediaType` NON FIABLE : le disque dur WD10EARX (D:) est déclaré 0 « non précisé » et refuse la question de la
  pénalité de recherche (IOCTL_STORAGE_QUERY_PROPERTY 7, erreur 31) ; seule la propriété TRIM (8) = false le révèle. Ordre :
  type déclaré → pénalité de recherche → TRIM. Disque USB 2.0 (E:) : ne répond à aucune → « type inconnu », Info. Alerte si
  disque dur ou moins de 20 Go / 10 % libres.
- Resizable BAR (`ResizableBarCheck`, information seulement : BIOS) : lu par `nvidia-smi -q -x` (`Core/Gpu/NvidiaSmi`,
  `Platform/Gpu/NvidiaSmiProvider`, System32 ou NVSMI, 10 s max, sans admin ; DOCTYPE vers un .dtd absent → DtdProcessing.Ignore) :
  BAR1 > 256 Mio = actif (RTX 3070 : 8192/8192). Signalé désactivé seulement pour Ampere / Ada / Blackwell. La lecture générique
  `Win32_DeviceMemoryAddress` NE VOIT PAS la fenêtre de 8 Gio → non utilisée ; AMD / Intel = « non lu », outil du fabricant.
- Jeux fenêtrés (`WindowedGamesCheck`) : `SwapEffectUpgradeEnable` dans `DirectXUserGlobalSettings` (fusion des paires,
  `GpuPreferenceString`) : 1 = OK (machine de dev, avec AutoHDREnable=1), 0 = à corriger (posé par des scripts « gaming »),
  absente = défaut NON documenté par Microsoft → Info + activation explicite proposée. L'Auto HDR force ce réglage.
- Plafond de FPS par jeu (carte « Pilote NVIDIA » ; `Core/Gpu/FrameRateCap`, `Platform/Gpu/NvidiaProfiles` +
  `NvidiaProfileSettingAccessor`, `App/Services/FrameCapService`) : NVAPI DRS (profils du pilote), en-têtes officiels
  github.com/NVIDIA/nvapi lus le 2026-10-03 : réglage `FRL_FPS_ID = 0x10835002` (0 = désactivé, ≤ 1023) = « Fréquence
  d'images maximale » du Panneau de configuration NVIDIA (confirmé par l'utilisateur le 2026-10-04), identifiants
  QueryInterface de nvapi_interface.h, codes de nvapi_lite_common.h. Structures écrites OCTET PAR OCTET (NVDRS_SETTING en
  pack(4) = 12320 octets ; NVDRS_APPLICATION_V4 = 20492 ; NVDRS_PROFILE_V1 = 4116). Chaînes NvAPI_UnicodeString passées en
  `ushort[]` de 2048 : un `char[]` est converti en ANSI par défaut et AUCUN profil n'était trouvé. FindApplicationByName avec
  le CHEMIN COMPLET = profil réellement appliqué (machine de dev : « Overwatch 2 », « PLAYERUNKNOWN'S BATTLEGROUNDS »,
  « Scrap Mechanic », « Squadron 42 - Star Citizen » ; Void Crew : aucun → profil « OptiGame - <exe> » créé, supprimé à
  l'annulation s'il est vide). Valeur NVIDIA prédéfinie = « non définie » (la retirer la rétablit). Lecture ET écriture sans
  admin (vérifié). `DiagDump -- --nvidia-profiles` (lecture) ; `--nvidia-selftest <exe INEXISTANT>` = journal → pilote →
  annulation sur un profil factice. Ne JAMAIS cliquer « Appliquer » sur l'instance de test : le pilote est celui du vrai PC.
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
  - Hors Steam (ou cartes citées par Steam inconnues) : PCGamingWiki (`Core/Rating/PcGamingWiki`, API MediaWiki :
    `opensearch` puis `parse&prop=wikitext`, bloc `{{System requirements}}` `|OSfamily = Windows`, champs minGPU/minGPU2…/
    recGPU…/minRAM/recRAM ; 6 jeux vérifiés le 2026-10-02, échantillons dans les tests). Titre IDENTIQUE au nom du profil
    seulement (sinon rien), seul le nom est envoyé, introuvable = nouvel essai après 7 jours. Licence CC BY-NC-SA : source
    citée + lien « Voir sur PCGamingWiki ». Écartés : IGDB (pas de configuration requise dans l'API), Can You Run It (ni API
    ni conditions publiées ; un mauvais identifiant affiche un AUTRE jeu sans erreur), recherche Steam par nom (packs, DLC).
    Client commun : `Platform/Library/GameRequirementsClient` (cache `requirements.json`, clés appid ou `pcgw:<nom>`).
  - Mesure : `Platform/Measurement/AutoCapture` = 1 capture PresentMon de 60 s après 4 min de partie, session ETW
    `OptiGame_AutoCapture` (jamais celle de la page Mesures), 5 dernières gardées par jeu, désactivable (Paramètres).
    Note = fluidité (60 % FPS moyens + 40 % 1 % low) / min(fréquence de l'écran, 120 Hz), médiane des 5 dernières captures.
  - Réglages du jeu ILLISIBLES par OptiGame : l'utilisateur indique le sien (`GameProfile.GraphicsPreset`, modifié seulement
    par `ProfileStore.SetGraphicsPreset`), copié dans chaque capture ; non indiqué = conseil relatif, sans réglage conseillé.
  - Limitation (`GameRatings.Classify`) d'après `FrameLoad` (Σ GPUBusy / Σ FrameTime, Σ CPUWait / Σ FrameTime, colonnes
    vérifiées sur de vrais CSV 2.6.0) : GPU ≥ 85 % = carte graphique ; sinon attente ≥ 15 % ou FPS ≈ fréquence = plafond ;
    sinon processeur. Réel : Overwatch plafonné 76 %/43 %, en V-Sync 82 %/64 % ; Void Crew 98 %/2 %. Captures antérieures :
    charge recalculée une fois depuis le CSV (`GameRatingService`, `CaptureStore.SetLoad`).
  - Bridage de la carte NVIDIA (`Core/Gpu/GpuSampling`, `NvidiaSmiProvider.SampleAsync`, `DiagDump -- --gpu-sample [s]`) :
    pendant les 60 s de la mesure auto, UN processus `nvidia-smi --query-gpu=… -lms 1000` (champs `clocks_event_reasons.*`
    vérifiés avec --help-query-gpu, pilote 617.14), arrêté à la fin. Résumé dans `CaptureRecord.GpuHealth`, carte la plus
    utilisée, seuil 10 % des relevés. Thermique (sw/hw_thermal) → conseil refroidissement ; hw_slowdown SANS thermique →
    alimentation ; sw_power_cap = NORMAL à pleine charge (GPU Boost), seulement affiché. clocks.max.gr (2100) = maximum
    absolu, pas la fréquence de boost : non utilisé.
- Jeux Steam possédés non installés + genres/types (« Mes jeux » ; `Core/Library/SteamBinaryCache` + `SteamOwnedGames`,
  `Platform/Library/SteamOwnedLibrary`, `DiagDump -- --steam-owned`) : caches BINAIRES du client, format non documenté par
  Valve (SteamDB) : `appcache\appinfo.vdf` v29 (magic 0x07564429, clés = index d'une table de noms en fin de fichier ;
  entrée = appid, taille, 60 octets d'en-tête puis KeyValues ; common/name, type, genres, category_N) et
  `appcache\packageinfo.vdf` v28 (magic 0x06565528, clés texte, pas de taille : parcourir les KeyValues). Autre version =
  FormatException, jamais de lecture approximative. Possédé = type « game » + licence d'un paquet ≠ 0 (le paquet 0 donne
  Dota 2, TF2, Spacewar à tous) + dossier dans `appcache\librarycache` (5 licences sans : FOR HONOR, Hunt, R.E.P.O.…
  écartées). Machine de dev : 738 apps, 380 paquets, 277 jeux, 271 non installés. Jaquettes locales :
  `librarycache\<appid>\library_600x900.jpg` ou `…\<hash>\library_capsule.jpg` (300×450), rien n'est téléchargé.
  Noms français des genres / catégories relevés sur appdetails (l=french) : `SteamTaxonomy`. Installer =
  `"steam.exe" -- "steam://install/<appid>"` via `UnelevatedLauncher` (fenêtre d'installation de Steam).
- Autres lanceurs, jeux INSTALLÉS (« Rechercher des jeux installés » ; `Core/Library/StoreLaunchers`,
  `Platform/Library/StoreLibraries`) : Epic = `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON) ; GOG =
  HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\<id>. Lanceurs pris en charge : Steam, Epic et GOG SEULEMENT (Ubisoft Connect et
  EA app abandonnés par l'utilisateur le 2026-10-04 ; leurs jeux restent ajoutables via un dossier de jeux).
  Profil créé en mode « Lanceur » avec la commande EXACTE des raccourcis que les lanceurs créent (relevés le 2026-10-04) :
  Epic `com.epicgames.launcher://apps/<ns>%3A<item>%3A<app>?action=launch&silent=true`,
  GOG `GalaxyClient.exe /command=runGame /gameId=<id> /path="…"` ; programme = celui du protocole
  (HKCR\<protocole>\shell\open\command).
- Autres lanceurs, jeux POSSÉDÉS non installés (section grisée + filtre « Plateforme » ; `Core/Library/StoreCatalogs`,
  `Platform/Library/StoreOwnedLibrary` + `StoreCoverCache`, `DiagDump -- --store-owned`) : Epic = `Data\Catalog\catcache.bin`
  (base64 → JSON ; jeu = catégorie « games » + `mainGameItem.id` vide ; 348 jeux) — SEULE source Epic (choix utilisateur : la
  liste Epic de Galaxy est périmée). GOG Galaxy = `galaxy-2.0.db` (SQLite, Microsoft.Data.Sqlite) lue sur une COPIE (db + -wal
  + -shm dans %TEMP%, supprimée ensuite) : SEULEMENT les jeux GOG (gog_), visibles et hors DLC (intégrations Epic / Ubisoft /
  EA de Galaxy ignorées : périmées, refusées par l'utilisateur). Genres de Galaxy (anglais)
  regroupés dans les genres Steam en français.
  Jaquettes : CDN `cdn1.epicgames.com` (`?h=528&w=396&resize=1`, ~11 Ko) et `images.gog.com` (.webp → .jpg), téléchargées
  une fois dans `covers\stores` quand la section est affichée, par lots de 4 pris dans l'ordre de la grille filtrée (le filtre
  choisi passe en premier). Installer : Epic = adresse des raccourcis avec `action=install` ; GOG =
  `GalaxyClient.exe /urlProtocol="goggalaxy://openGameView/gog_<id>"` — ces deux actions ne sont PAS vérifiées en vrai par
  l'agent (test utilisateur). Machine de dev : 347 Epic, 13 GOG.
- Jeux désinstallés (« Mes jeux » : bouton « Actualiser », pastilles « Désinstallé » / « Disque absent », bandeau ;
  `Core/Library/GameInstallation`) : exe du profil absent + racine du disque présente = désinstallé ; racine absente (disque
  externe, lecteur réseau) = « Disque absent », JAMAIS désinstallé. Vérifié à chaque rechargement de la grille. Aucun profil
  n'est retiré automatiquement : « Retirer de Mes jeux… » après confirmation (le jeu en cours est exclu). « Jouer » refuse un
  jeu désinstallé avec un message. Machine de dev (2026-10-04) : Absolute Drift, Steep, The Sims 3, Rites of War désinstallés.
  Retirer UN jeu (mauvais exe ajouté…) : corbeille au survol de la jaquette, menu contextuel ou page du jeu (section « Profil »)
  → `LibraryViewModel.RemoveGame` : confirmation, jamais le jeu en cours, profil seul supprimé (le jeu reste installé).
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
- Interface : audit UI/UX complet du 2026-10-05 (v1.2.1) dans `docs/ui-ux-audit-2026-10-05.md` : système de design actuel,
  contrastes calculés, problèmes priorisés avec preuves fichier:ligne, contraintes à préserver, glossaire (un terme par concept).

## Distribution (installeur)

- Version : `<Version>` de `Directory.Build.props` (affichée dans Paramètres > Mises à jour, écrite au journal au démarrage).
- `scripts\build-installer.ps1` → `artifacts\installer\OptiGame-Setup-<version>.exe` : publication AUTONOME win-x64
  (.NET inclus, ReadyToRun, sans .pdb, ressources traduites `fr` seulement : ≈ 141 Mo publiés), PresentMon ajouté, puis
  Inno Setup 6 (`installer/OptiGame.iss`, en français). Le script se lit en UTF-8 AVEC BOM (Windows PowerShell 5.1).
- Installeur : `AppId` {BEC983EC-07E6-4D4C-A824-72F7135018FA} DÉFINITIF (mises à jour). Program Files IMPOSÉ
  (`DisableDirPage`), admin (exe lancé élevé : jamais dans le profil ni sur un autre disque, où d'autres programmes peuvent
  écrire). Avant mise à jour ou désinstallation : fermeture propre par `quit.request` (partie en cours restaurée), sinon arrêt
  avec message. Désinstallation : tâche planifiée « OptiGame » et `{app}\updates` supprimés, `%LocalAppData%\OptiGame` GARDÉ,
  rappel que les corrections restent (à annuler avant).
  Lancement après installation au nom de l'utilisateur d'origine (`runasoriginaluser`), pas du compte admin qui a validé.
- PresentMon 2.6.0 fourni dans `tools\` à côté de l'exe (`PresentMonRunner` cherche d'abord `%LocalAppData%\OptiGame\tools`,
  puis celui de l'appli) : SHA-256 B2A706BC…88F1AF épinglé + signature « O=Intel Corporation » vérifiée
  (Get-AuthenticodeSignature) ; licence MIT dans `installer/THIRD-PARTY-NOTICES.txt` (installé avec l'appli).
- CI `.github/workflows/installer.yml` (windows-latest) : tests Core, `DiagDump --update-check`, installeur, installation /
  démarrage / mise à jour par-dessus l'appli lancée (/RELAUNCH) / désinstallation silencieux, installeur en artefact
  « OptiGame-Setup » ; tag `v*` → brouillon de version GitHub, REFUSÉ si le tag ≠ `v<Version>` (le 2026-10-04, un tag v1.1.0
  posé sur le commit de la 1.0.0 avait publié en brouillon un installeur 1.0.0). Lancée à la demande et à chaque changement
  de l'installeur.
- Publier une version : monter `<Version>`, mettre sur main (accord de l'utilisateur pour chaque version), puis pousser le tag
  (`git tag vX.Y.Z origin/main`, `git push origin vX.Y.Z`) : une session LOCALE y est autorisée par l'utilisateur depuis le
  2026-10-05 ; la session cloud ne le peut pas (erreur 403), l'utilisateur le fait alors. L'utilisateur publie le brouillon
  créé par la CI.
- Non signé : Windows SmartScreen avertit au premier lancement de l'installeur (« Informations complémentaires »).

## Mises à jour automatiques

- Source : API GitHub `releases/latest` (ni brouillon ni préversion) ; lecture et décision dans `Core/Updates/AppReleases`
  (format relevé le 2026-10-04 sur une vraie réponse, échantillon dans `tests/…/Updates/Samples`) : tag `vX.Y.Z`, fichier
  `OptiGame-Setup-X.Y.Z.exe` de CE tag (`browser_download_url` exacte), `state` = uploaded, `size`, `digest` = « sha256:… »
  calculé par GitHub. Téléchargement redirigé vers `release-assets.githubusercontent.com` (relevé), rien d'autre accepté.
- Seule la copie INSTALLÉE dans Program Files se met à jour (`Platform/Updates/InstalledCopy` : clé de désinstallation
  `{AppId}_is1`, valeur InstallLocation = dossier de l'exe ; `UpdatePolicy.WhyNoSelfUpdate`) ; une copie de développement
  peut chercher, jamais installer. Installeur téléchargé dans `{app}\updates` (réservé aux administrateurs), taille et SHA-256
  vérifiés, refusé = `.non-verifie`. Lancé par `IPrivilegedOperations.StartAppUpdate` : fichier ouvert en lecture seule
  (partage lecture) de la nouvelle vérification jusqu'au lancement, `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART
  /RELAUNCH=minimized|window /LOG=<logs>\update.log` ; OptiGame se ferme, l'installeur le remplace puis le relance
  (`DeinitializeSetup`, même si l'installation échoue), avec ses droits : pas d'UAC.
- `App/Services/UpdateService`, réglage `UpdateMode` (Automatique par défaut / Me prévenir / Désactivé) : recherche 2 min
  après le démarrage puis toutes les 24 h, reportée pendant les parties (GameTimeGate « update-check ») ; installation
  automatique seulement si `UpdatePolicy.WhyNotNow` est nul (aucune partie, fenêtre fermée, pas de plein écran), sinon nouvel
  essai toutes les 10 min, et 30 s après la fermeture de la fenêtre (pas tout de suite : quitter OptiGame la ferme aussi).
  Après une mise à jour (`LastRunVersion` plus ancienne) : notification + bandeau « mis à jour », anciens installeurs
  supprimés. Paramètres > Mises à jour (Rechercher / Installer maintenant / Nouveautés) ; bandeau dans la fenêtre.
- Vérifié en vrai le 2026-10-05 sur la machine de dev : 1.2.0 installée, 1.2.1 publiée → « Rechercher maintenant »,
  téléchargement, installation à la fermeture de la fenêtre, OptiGame relancé de lui-même en 1.2.1.
- `DiagDump -- --update-check [--download]` : dernière version publiée, fichier, empreinte, clé de désinstallation ;
  `--download` vérifie aussi le téléchargement complet (dossier temporaire).
- Sans signature de code, une mise à jour n'est authentique que si le compte GitHub l'est (double authentification) ;
  « immutable releases » (réglage du dépôt) empêche de modifier une version publiée.

## Commandes

```powershell
dotnet build OptiGame.slnx
dotnet test OptiGame.slnx
# Compiler et relancer l'appli (ferme proprement l'instance en cours via quit.request, qui verrouillerait les DLL ;
# restaure une éventuelle session de jeu). `dotnet run` échoue (erreur 740) car il ne peut pas déclencher l'UAC.
.\scripts\dev-run.ps1
# Captures PNG de chaque page (refonte UI : avant / après) → artifacts\ui-snapshots\<Label>\ ; mode caché « --snapshot »
# (App/Snapshots/PageSnapshots), données copiées dans %TEMP%\OptiGame-ui\data, sans élévation, hors écran, rien ne démarre.
.\scripts\ui-snapshots.ps1 -Label avant [-Sizes 880x600,1240x860] [-Game "Portal"] [-Reseed]
.\scripts\build-installer.ps1                # installeur (Inno Setup 6 : winget install --id JRSoftware.InnoSetup -e)
dotnet run --project tools/OptiGame.DiagDump   # diagnostic lecture seule en console, sans élévation
```
