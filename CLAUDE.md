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

Règle de dépendance : `App → Platform → Core`. La logique de décision (statut d'un check, restauration)
vit dans Core ; Platform ne fait que lire/écrire le système.

## Faits vérifiés sur la machine de dev (Windows 11 25H2, build 26200, AtlasOS)

- `HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled` : peut être **absente** → absent = Mode Jeu activé.
- `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode` : peut être **absente** → lire l'état réel
  de HAGS via `D3DKMTQueryAdapterInfo(KMTQAITYPE_WDDM_2_7_CAPS)` ; registre 2 = ON, 1 = OFF (redémarrage requis).
- Enregistrement en arrière-plan : `HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled`
  (+ `AppCaptureEnabled`) ; interrupteur global : `HKCU\System\GameConfigStore\GameDVR_Enabled`.
- `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` : valeurs `Clé=Valeur;` par chemin d'exe → **fusionner**
  `GpuPreference=N;` avec les paires existantes (ex. `AutoHDREnable`).
- VBS/HVCI : WMI `root\Microsoft\Windows\DeviceGuard` / `Win32_DeviceGuard` ; désactivation avancée via
  `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled`.
- `powercfg` a une sortie localisée → ne jamais la parser ; utiliser `powrprof.dll`. Les plans personnalisés
  (ex. « Atlas Power Scheme ») sont un statut Info.
- Une valeur de registre absente se sauvegarde comme `Absent` et se restaure par **suppression**.
- CSV réel de PresentMon 2.6.0 (`--v2_metrics`) : colonnes `FrameTime`, `CPUBusy`, `GPUTime`, `DisplayedTime`… SANS
  préfixe `Ms`, contrairement à sa documentation. Toujours valider un format sur un vrai fichier, et ne jamais
  supprimer un fichier qu'on n'a pas réussi à lire.
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
