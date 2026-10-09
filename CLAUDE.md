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
                         nouveaux jeux Steam en UN bandeau) → GamePageViewModel (fiche : fil d'Ariane FLOTTANT (hors du défilement), bannière plein cadre avec jaquette, 55 % de la hauteur, au moins son
                         contenu ; Jouer / « Arrêter l'optimisation… » + « … », 3 onglets GameTab Vue d'ensemble / Optimisation /
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
PresentMon se règle dans Paramètres › Mesures (MeasuresViewModel.ChoosePresentMon) ; Paramètres en onglets (SettingsTab,
sélecteur `Segment` comme la fiche du jeu), une carte `SettingsCard` de Controls/SettingRow par onglet.

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
- « Voir sur … » DANS le lanceur d'abord (`GameLauncher.OpenStorePageInLauncher`), navigateur en recours. Vérifié par captures
  le 2026-10-06 : Epic = `com.epicgames.launcher://store/browse?q=<titre>` (le jeu en 1er résultat ; les pages produit
  `store/p/…`, `store/fr/p/…`, `store/product/…/home` donnent TOUTES « Page introuvable », même Fortnite), envoyé comme
  l'installation (lanceur fermé : démarré seul, adresse 8 s après sa fenêtre) ; GOG = `GalaxyClient.exe /urlProtocol=
  "goggalaxy://openStoreUrl/embed.gog.com/game/<page>"` (commande « openStoreUrl » de l'ExternalUrlHandler de GalaxyClient.exe).
- Barre « Lanceurs » (en-tête de Mes jeux ; `ViewModels/LaunchersViewModel`, `Platform/Processes/LauncherControl`,
  `Core/Library/LauncherApps`) : icône par lanceur installé, point vert s'il est ouvert, menu Ouvrir / Afficher / Fermer…
  (confirmé, refusé pendant une partie). État relu à l'affichage de Mes jeux et au retour sur la fenêtre, jamais en boucle.
  Fermer : Steam = `steam.exe -shutdown` ; Epic et Galaxy n'ont PAS de commande pour quitter (chaînes de leurs exe cherchées) →
  arrêt de leurs processus de la session, dans leur dossier (Epic : `Epic Games\Launcher` seulement, jamais Epic Online
  Services qui sert aux jeux ; Galaxy : son dossier, le service GalaxyCommunication est hors session).
- « Voir sur Epic Games / GOG » (fiche d'un profil créé depuis leur lanceur, jaquettes non installées) : `Core/Library/StorePages`
  + `Platform/Library/StorePageResolver`, vérifié le 2026-10-06 (`DiagDump -- --store-page`). Le catalogue Epic n'a PAS l'adresse
  de page : `store-content.ak.epicgames.com/api/content/productmapping` (espace de noms → page, 231 jeux sur 348) ; GOG :
  `api.gog.com/products/<id>` → `links.product_card` (www.gog.com seulement). Sinon : recherche du titre sur le magasin.
  Ouvert par le navigateur PAR DÉFAUT, sans admin (`Core/Launching/BrowserCommand` : commande de UserChoice https, Firefox =
  `-osint -url "%1"`) — jamais explorer.exe, qui ouvre « Documents » pour une adresse avec « ? » et « & » (`OpenStoreWebPage` :
  https store.epicgames.com / www.gog.com seuls).

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
- Branchement des écrans (contrôle `DisplayLinkCheck` du Diagnostic, conseil matériel sans correction ; `Core/Display/Edid`,
  `Platform/Display/DisplayLinkReader`) : QueryDisplayConfig + GET_SOURCE_NAME (« \\.\DISPLAY6 ») + GET_TARGET_NAME (connexion
  = outputTechnology : 5 HDMI, 10 DisplayPort, 18 USB-C ; chemin « \\?\DISPLAY#SAM711A#<instance>#{…} » → EDID dans
  HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\<modèle>\<instance>\Device Parameters, lisible sans admin). EDID : modes
  détaillés du bloc de base (1er = natif), de CTA-861 (0x02) et de DisplayID (0x70, blocs 0x03 / 0x22). La PLAGE de l'EDID
  n'est PAS utilisée (BenQ GW2470 : 76 Hz annoncés, écran 60 Hz). Vérifié le 2026-10-07 : Samsung LC34G55T 165 Hz à
  3440×1440 SEULEMENT dans DisplayID (100 Hz dans le bloc de base), BenQ 60 Hz, Acer KG241Q 143,85 Hz ; EDID réels (séries
  effacées) dans les tests. Signalé : fréquence native de l'EDID > modes de Windows à cette résolution + 5 Hz (câble, port
  HDMI 1.4, adaptateur ; modes en portrait = dimensions inversées) ; PC de bureau, écran PRINCIPAL sur la carte intégrée
  alors qu'une carte dédiée existe (jamais sur portable ni pour un écran secondaire).
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
- Réglages du pilote NVIDIA, catalogue : `NvidiaProfiles.AvailableSettings` (NvAPI_DRS_EnumAvailableSettingIds 0xf020614a +
  GetSettingNameFromId 0xd61cbe6e) et `AvailableValues` (EnumAvailableSettingValues 0x2ec39f90, NVDRS_SETTING_VALUES pack(4) =
  414112 octets) ; `Knows(id)`. `DiagDump -- --nvidia-settings <mots>` (noms donnés par le pilote, valeurs admises, valeur
  appliquée à chaque jeu). Pilote 617.42 : 130 réglages.
- DLSS le plus récent par jeu (carte « DLSS (pilote NVIDIA) », onglet Optimisation ; `Core/Gpu/DlssOverride`,
  `Platform/Gpu/DlssLibrary`, `App/Services/DlssOverrideService`) : réglages OFFICIELS de NvApiDriverSettings.h (lu le
  2026-10-07) NGX_DLSS_SR_OVERRIDE 0x10E41E01 = 1 + NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION 0x10E41DF3 = 0x00FFFFFF
  (RENDER_PRESET_Latest), dans le profil du pilote du jeu (fichiers du jeu intacts),
  journal « game.dlss-latest.<profil> ». Écriture + annulation vérifiées sur exe FACTICE (`--nvidia-selftest`, profil créé
  puis supprimé). Carte seulement si nvngx_dlss.dll est trouvée (`DlssOverride.SearchRoot` : dossier steamapps\common\<jeu>,
  racine Unreal, sinon dossier de l'exe ; 10 niveaux) ou un remplacement existe : Overwatch 3.7.20, Void Crew 3.1.11, ARC
  Raiders / PUBG (Engine\Plugins\…), Star Citizen (Bin64). Cible générique `KnownSettings.NvidiaSetting(exe, id)` (le plafond
  de FPS garde le nom « 0x10835002 »). Écartés (principe 3) : « Power management mode » Performances maximales (effet sur les
  FPS quasi nul), mode faible latence (Maximum pre-rendered frames : sans effet en DX12 / Vulkan, API du jeu inconnue).
  Remplacement de nvngx_dlss.dll dans le dossier du jeu NON repris : fichiers du jeu modifiés, risque anti-triche. Ne citer
  AUCUN autre logiciel dans l'interface (demande de l'utilisateur, 2026-10-07).
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
  - Avis sur une mise à jour NVIDIA (`Core/Drivers/DriverConfidence`, encadré sous la carte graphique) : FAITS publiés par
    NVIDIA seulement (pas de forums) croisés avec les noms de Mes jeux (`DriverConfidences.Mentions` : nom entier, mots
    entiers, casse / accents / ™ / ponctuation ignorés, < 4 lettres ignoré). Sources (`NvidiaReleaseNotes`, relevées le
    2026-10-07) : JSON du service = titre « Game Ready for … », listes « Fixed … Bugs » (numéros retirés), lien du PDF dans
    OtherNotes ; PDF des notes de version (`PdfText` : flux FlateDecode, Tj/TJ, polices TrueType WinAnsi, octaux \222 \223 ; PAS
    un lecteur PDF général) = section « Open Issues in Version X » (DERNIÈRE occurrence), éléments après « > », jusqu'à
    « Issues Not Caused by NVIDIA », pieds de page « RN - 08399- 617.14_ v01 | 15 … » retirés (espaces variables). Vérifié sur
    617.14, 616.56, 591.86 (`DiagDump -- --nvidia-release-notes <versions> [--excerpt <dossier>]` ; extraits du texte dans les
    tests, jamais les PDF de NVIDIA). PDF lu seulement sur *.download.nvidia.com, 20 Mo max, gardé en mémoire par version.
    Niveaux : Non vérifié (PDF illisible : jamais « sans risque ») / Prudence (problème ouvert qui cite un de vos jeux) /
    Aucun problème connu / Conseillée (+ correction ou Game Ready pour vos jeux) ; publiée depuis < 3 jours = signalé.
    AMD : cartes graphiques AMD non gérées par la page Pilotes (chipset seulement) ; pas de machine AMD pour vérifier.
  - Avant / après un changement de pilote (`Core/Drivers/DriverImpact`, encadré sous la carte graphique) : chaque mesure note
    `CaptureRecord.GpuDriver` (« 617.42 », depuis le 2026-10-07 ; WMI 32.0.16.1742) ; par jeu, médiane des FPS moyens sous le
    pilote actuel contre le précédent, au MÊME réglage graphique ; baisse ≥ 5 % = signalée avec « Restaurer le pilote ».
    Aperçu : ui-snapshots `-Only 3b` (avis et bilan d'exemple, en mémoire).
- Nouveaux jeux Steam (`Core/Library/NewSteamGames`, `Platform/Library/SteamLibraryWatcher`, bandeau de « Mes jeux ») :
  FileSystemWatcher sur `steamapps\appmanifest_*.acf` de chaque bibliothèque, 3 s après la dernière écriture ; proposé si
  `StateFlags` a le bit 4 (entièrement installé ; 1026 = téléchargement en cours), sans profil et absent de
  `settings.SteamKnownAppIds` (null = premier passage : l'existant est mémorisé, pas proposé). Ajouté ou ignoré = mémorisé.
- Genres et types de Mes jeux, TOUS magasins (`Core/Library/GameTaxonomy` = vocabulaire français commun, `GameTags`) : genres et
  catégories du magasin Steam, genres + thèmes de GOG Galaxy (`originalMeta` = noms IGDB, relevé le 2026-10-06 :
  `DiagDump -- --galaxy-meta`), complétés pour tous par IGDB (`App/Services/GameTagService`, `Platform/Library/GameTagCache` =
  taxonomy.json par nom normalisé, introuvable = nouvel essai après 30 jours ; seulement avec identifiants IGDB, jamais pendant
  une partie). IGDB vérifié en vrai le 2026-10-06 (`DiagDump -- --igdb-taxonomy <noms>`, échantillon dans les tests) :
  `search` ne renvoie RIEN dans /v4/multiquery (« [] ») → requête multiple par nom exact `where name ~ "…"` (10 par appel), puis
  `search` individuel sur /games pour les autres ; homonymes (« Hades ») départagés par `total_rating_count`. Machine de dev :
  575 jeux sur 619 trouvés en ≈ 4 min (une seule fois), 46 genres. Sans IGDB : texte d'aide sous les filtres (`ShowsTagsHint`).
- Note des jeux (`Core/Rating`, `App/Services/GameRatingService`, pastille des jaquettes + carte « Note sur ce PC ») :
  - Estimation : configuration requise de Steam (`store.steampowered.com/api/appdetails?appids=<id>&filters=basic&l=english`,
    `pc_requirements.minimum/recommended` en HTML, lignes « Graphics: » et « Memory: » ; 4 vraies réponses dans les tests),
    gardée 30 jours dans `requirements.json`. Cartes comparées par `GpuPerformance` (indices APPROXIMATIFS, GTX 1060 = 100,
    affichés comme estimation), résolution ramenée au 1080p (pixels^0,7). Recommandé atteint = « Élevé ». Écran = l'écran
    PRINCIPAL de Windows (`GameRatings.GamingDisplay`, DISPLAY_DEVICE_PRIMARY_DEVICE, vérifié : DISPLAY6 3440×1440 165 Hz), relu
    à chaque note. Fréquence : la NOTE reste calculée à 60 FPS (ce que visent les configurations requises, souvent prudentes :
    Void Crew estimé « Moyen », mesuré en Ultra à 95-153 FPS le 2026-10-06) ; le réglage AFFICHÉ est celui pour
    `TargetFps` = fréquence bornée à 30-120 (puissance demandée × cible/60). Upscaling (DLSS seulement sur RTX, sinon FSR /
    XeSS) en mode Qualité (2/3 par axe) conseillé s'il fait gagner un cran, jamais une résolution sous celle de l'écran
    (flou) ; grande marge = anticrénelage natif (DLAA…).
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
  - Programmes gourmands (`Core/Measurement/BackgroundLoad`, `Platform/Measurement/ProcessCpuSampler`, `DiagDump -- --background
    [s]`) : DEUX relevés du temps processeur des processus de la session (début et fin des 60 s de la mesure auto), rien
    entre ; % du processeur ENTIER (tous les threads), processus d'un même exe additionnés, ≥ 2 %, 5 au plus ; écartés le jeu,
    les composants de Windows, ProfileValidator.ProtectedProcesses, PresentMon, nvidia-smi. Vérifié le 2026-10-06 : une boucle
    sur un cœur = 8,3 % sur 12 threads. `CaptureRecord.Background` ; onglet Optimisation (sous « Programmes à fermer ») :
    « Fermer pendant les parties » ajoute l'exe à l'éditeur (puis « Enregistrer ») ; cités dans le conseil si le processeur
    limite.
  - Réglage du jeu : choisi par l'utilisateur (`GameProfile.GraphicsPreset`, modifié seulement par
    `ProfileStore.SetGraphicsPreset`), sinon « Automatique » = lu dans le jeu (ci-dessous), copié dans chaque capture
    (`InGameSettingsReader.PresetForCapture`) ; aucun = conseil relatif, sans réglage conseillé.
  - Réglages LUS dans les fichiers du jeu, lecture seule (`Core/InGame/InGameSettings`, `Platform/InGame/InGameSettingsReader`,
    `DiagDump -- --ingame-settings`), vérifiés le 2026-10-06 : Unreal Engine 4/5 = `%LocalAppData%\<projet>\Saved\Config\
    <Windows|WindowsClient|WindowsNoEditor>\GameUserSettings.ini` (projet = dossier au-dessus de Binaries\Win64 : PUBG TslGame,
    ARC Raiders PioneerGame) ; qualité = médiane des `sg.*` (hors ResolutionQuality = échelle de rendu, et LandscapeQuality,
    fixé par le jeu) en niveaux du moteur (0 Bas … 3-4 Ultra) SAUF échelle propre au jeu (`UnrealSettings.ScaleOf`, par projet :
    TslGame = PUBG, Très bas … Ultra sur 0-4, niveau 2 = « Moyen » confirmé par l'utilisateur) ; choix manuel ≠ jeu = signalé,
    ResolutionSizeX/Y, FullscreenMode, bUseVSync, FrameRateLimit (> 500 = sans limite), upscaling si ResolutionScalingMethod /
    UpscalingMethod + <méthode>Mode / QualityOption. Unity = écran SEULEMENT (`<exe>_Data\app.info` → HKCU\Software\<éditeur>\
    <jeu>, « Screenmanager … _h<hash> ») : UnityGraphicsQuality NON lu (Void Crew : 1 alors qu'il est en Ultra ; ses vrais
    réglages sont dans son propre Settings.json). Overwatch, Scrap Mechanic, Star Citizen, Portal 2 : rien de lisible. Les
    conseils s'en servent : cause du plafond (V-Sync / limite lue), upscaling déjà actif (mode plus rapide plutôt
    qu'« activez »), estimation avec upscaling si déjà activé.
  - Qualité ÉCRITE dans un jeu Unreal (carte « Qualité graphique (fichier du jeu) », onglet Optimisation ;
    `Core/InGame/UnrealQuality` + `IniText`, `Platform/InGame/IniFileAccessor` = kind `ini-value`, Path « fichier|section »,
    `App/Services/InGameQualityService`) : tous les `sg.*` lus (hors ResolutionQuality / LandscapeQuality) mis au niveau choisi,
    journal fixes.json « game.unreal-quality.<profil> », confirmé, « Restaurer l'original… ». Seule la ligne change (IniText),
    même encodage et fins de ligne, fichier remplacé d'un coup ; fichier disparu = jamais recréé. Aller-retour vérifié OCTET
    PAR OCTET sur un extrait réel de PUBG (tests Platform). REFUSÉ jeu ouvert (il réécrit le fichier en quittant) — sauf
    restauration depuis le Diagnostic, qui ne vérifie pas. Niveau d'un réglage : `UnrealQuality.LevelFor` (Bas = le plus haut
    des « Bas », Ultra = le premier des « Ultra » : Épique, pas Cinématique). Non testé en vrai sur les jeux de l'utilisateur
    (son choix).
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
- Présentation des jeux Steam sur la fiche (carte « À propos du jeu » ; `Core/Library/SteamStoreAbout`, `Platform/Library/
  StoreAboutClient` = store-about.json, 7 jours, réponses BRUTES relues) : appdetails `l=french&filters=basic,metacritic` (6,8 Ko
  au lieu de 29 ; `metacritic` n'est PAS dans basic, absent pour ARC Raiders) et `appreviews/<id>?json=1&language=all&
  purchase_type=all&num_per_page=0&l=french` (`review_score` 1-9 + `review_score_desc` en français : « très positives » ;
  0 = trop peu d'avis, « 3 évaluations » ; « aucune évaluation » = total 0). Vérifié le 2026-10-07, échantillons dans les
  tests. Steam ne publie pas son arrondi : % arrondi au plus proche, « toutes langues » (la page du magasin affiche « dans votre
  langue »). HTML de about_the_game réduit à du texte (titres, puces ; images et vidéos retirées), jamais affiché. Lu à
  l'ouverture de la fiche seulement, cache d'abord, mise à jour reportée à la fin d'une partie (GameTimeGate « store-about ») ;
  « Voir les critiques » = navigateur par défaut, www.metacritic.com SEULEMENT (`GameLauncher.OpenPressPage`).
  Bandes-annonces (`Controls/TrailerPlayer`, paquet Microsoft.Web.WebView2 1.0.4258.31) : Steam ne fournit PLUS de MP4 pour les
  vidéos récentes (movie480.mp4 = 404 pour ARC Raiders), seulement HLS / DASH (`movies[].hls_h264`) ; le moteur WebView2 de
  Windows (154, présent sous AtlasOS sans le navigateur Edge) lit le HLS NATIVEMENT (vérifié le 2026-10-07). Vignette (293×165
  anciennes, 600×337 récentes ; aucune autre taille fiable) dans `covers\trailers`. Moteur créé AU CLIC, détruit si la vidéo
  n'est plus visible (fiche quittée, autre onglet, fenêtre fermée) ou qu'une partie commence (`IsGameRunning`) ; mesuré : 6
  processus, ≈ 220-245 Mo privés pendant la lecture, 0 processus après (ui-snapshots `-Only 6f,6g`, journal « Bande-annonce »).
  Dossier `webview\` (cache disque 10 Mo), page locale verrouillée (CSP médias *.steamstatic.com, une seule navigation, ni
  menu, ni outils, filtre de réputation coupé). La vidéo (fenêtre HWND) n'apparaît pas sur les captures, et WPF ne la rogne PAS :
  `TrailerPlayer.UpdateClip` (SetWindowRgn à chaque mise en page) la limite à la zone qui défile, moins les éléments
  `TrailerPlayer.IsAboveVideo` (fil d'Ariane flottant) ; vérifié (`-Only 6h`) : hors zone = masquée, sous le fil = trouée. La
  variante WebView2CompositionControl (sans ce problème) est écartée : capture d'écran interne, images moins fluides. Plein écran (bouton du
  lecteur, double-clic ; Échap pour sortir) : `ContainsFullScreenElementChanged` → le contrôle WebView2 est DÉPLACÉ (pas recréé)
  dans une fenêtre sans bord agrandie sur l'écran d'OptiGame, puis remis dans la fiche ; Alt+F4 le remet avant destruction de
  la fenêtre (Closing). Vérifié (ui-snapshots `-Only 6h`, fenêtre hors écran) : lecture continue, 5,4 → 8,5 → 11,5 s.
  Jeux hors Steam (`LibraryViewModel.AboutSources`, même cache, clés « gog:<produit> » / « igdb:<numéro> ») : jeu GOG
  (`StorePages.FromProfile`) → `Core/Library/GogStoreAbout` : `api.gog.com/products/<id>?expand=description,videos&locale=fr-FR`
  (description.full en HTML, parfois en ANGLAIS malgré fr-FR ; encarts `<p class="module">` = publicité de GOG, retirés ; vidéos
  YouTube « embed » ou fast.wistia.net ; produit inconnu = 404) + `reviews.gog.com/v1/products/<id>/averageRating?reviewer=
  verified_owner` ({value sur 5, count} ; aucun avis = 0/0). Sinon, ou si GOG n'a rien : IGDB (`Core/Library/IgdbAbout`,
  `IgdbClient.AboutAsync`, `profile.IgdbGameId`, seulement avec identifiants) : summary ANGLAIS seulement (dit dans la carte),
  rating ≥ 10 votes, aggregated_rating ≥ 3 critiques (Void Crew : 1 seule, masquée), vidéos YouTube (noms en double numérotés).
  Vérifié le 2026-10-08 (échantillons dans les tests). Vidéos hors Steam = NAVIGATEUR (`GameLauncher.OpenWebVideo`, adresses de
  `Core/Library/WebVideo` seulement : youtube.com/watch?v=<11>, fast.wistia.net/embed/iframe/<id>). Aucun jeu GOG installé sur
  la machine de dev : carte GOG vue avec un profil FICTIF ajouté à la copie de données des captures (retiré ensuite).
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
  choisi passe en premier). Installer : Epic = adresse des raccourcis avec `action=install&silent=true` (vérifié le 2026-10-06
  par capture du lanceur ouvert : SANS silent il n'affiche que sa boutique, AVEC « Choisir l'emplacement de l'installation ») ;
  lanceur fermé = démarré AVEC la demande il reste caché et la perd → démarré SEUL (fenêtre en ≈ 6 s), puis la demande 8 s après
  l'apparition de sa fenêtre (`GameLauncher.SendWhenEpicIsReadyAsync`, 60 s max) ; GOG =
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
- WPF : un élément placé dans une propriété `object` d'un contrôle (`CoverTile.Actions`, affiché par un ContentPresenter du
  gabarit) n'est PAS dans l'arbre quand ses liaisons s'évaluent : `RelativeSource AncestorType=UserControl` y finit en PathError,
  jamais retentée → bouton SANS commande, et son clic retombe sur le parent (la jaquette lançait l'installation au lieu de
  « Voir sur … », constaté le 2026-10-06). Lier par le DataContext (cartes : `Owner.XxxCommand`). Contrôle : ui-snapshots
  `-Only 1z` (toutes les liaisons de commande des pages, « Liaisons : toutes actives »).
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
- Apparence (Inno Setup 6.7 MINIMUM, `#error` sinon ; la CI fait `choco upgrade innosetup` : ISCC.exe n'a pas de numéro de
  version lisible) : `WizardStyle=modern dark includetitlebar`, fond `#0E1014` (Color.Window), page d'accueil affichée,
  textes d'accueil / de fin réécrits (`[Messages]`). Images `installer/images/*.png` VERSIONNÉES, générées par
  `scripts\installer-images.ps1` (WPF, logo et couleurs de Theme.xaml) aux 7 tailles d'Inno Setup (100 → 250 %).
  Essai à l'écran : copie du .iss avec `PrivilegesRequired=lowest`, AUTRE AppId, AUTRE nom de processus dans [Code] et sans
  [UninstallRun] — sinon l'essai se prend pour une mise à jour et FERME l'OptiGame en cours (arrivé le 2026-10-06), et sa
  désinstallation supprimerait la tâche planifiée « OptiGame ».
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
- Signature : SignPath Foundation (gratuit, logiciels libres), choisie par l'utilisateur le 2026-10-06 ; code sous licence MIT
  (`LICENSE`, installé en LICENSE.txt), politique de signature dans `README.md` et de confidentialité dans `PRIVACY.md`
  (exigées par signpath.org/terms : mention « Free code signing provided by SignPath.io, certificate by SignPath Foundation »,
  rôles ; `PRIVACY.md` = liste EXACTE des connexions réseau, à tenir à jour si OptiGame contacte un nouveau service ; adresse
  fixe `AppReleases.PrivacyPolicy`, ouverte par Paramètres › Données › « Politique de confidentialité » et citée dans les
  notes de chaque version, qui disent aussi si l'installeur est signé). OptiGame ne collecte AUCUNE donnée (vérifié le
  2026-10-06 : 9 clients HTTP, aucune télémétrie). Désinstalleur : thème sombre et fond #0E1014 hérités (WizardBackColor,
  mesuré sur capture). Signés : OptiGame.exe,
  OptiGame.dll, OptiGame.Core.dll, OptiGame.Platform.dll, puis l'installeur (`.signpath/artifact-configurations/*.xml`, à
  recopier dans SignPath). JAMAIS les binaires d'autrui : le désinstalleur d'Inno Setup reste non signé (et ISCC refuse un
  SignTool qui ne signe pas : « the file does not have a digital signature »). CI : `build-installer.ps1 -Step Publish`,
  signature, `-Step Package`, signature ; seulement pour un tag ET si la variable de dépôt SIGNPATH_ORGANIZATION_ID existe
  (+ SIGNPATH_PROJECT_SLUG, SIGNPATH_SIGNING_POLICY_SLUG, secret SIGNPATH_API_TOKEN) ; chaque demande attend l'approbation
  manuelle de l'utilisateur dans SignPath (30 min max). Éditeur affiché par Windows : « SignPath Foundation ». Même signé,
  SmartScreen avertit tant que la réputation n'est pas faite (EV compris depuis 2024), mais elle passe d'une version à l'autre.
  Tant que SignPath n'est pas configuré : non signé, SmartScreen avertit au premier lancement (« Informations complémentaires »).

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
.\scripts\build-installer.ps1                # installeur (Inno Setup 6.7+ : winget install --id JRSoftware.InnoSetup -e)
dotnet run --project tools/OptiGame.DiagDump   # diagnostic lecture seule en console, sans élévation
```
