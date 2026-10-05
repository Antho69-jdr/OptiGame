<!-- Audit UI/UX du 2026-10-05 (v1.2.1, commit a32c982) : 8 lecteurs en parallèle + synthèse, problèmes de gravité haute recontrôlés dans le code. Numéros de ligne valables pour ce commit. Point de départ de la refonte UI/UX. -->

# Brief de refonte UI/UX d'OptiGame

> Ce brief s'adresse à l'agent chargé de refaire toute l'interface. Il synthétise un audit en 7 zones et une contre-vérification dans le code au commit `a32c982` (v1.2.1, 2026-10-05). Sauf mention contraire, les chemins partent de `src/OptiGame.App/`. Les numéros de ligne valent pour ce commit. Les contrastes ont été recalculés (WCAG 2.x, fonds « Soft » mélangés à leur opacité réelle).

---

## 1. Synthèse

1. OptiGame (WPF .NET 10, thème Fluent sombre et accent vert) compte 5 pages (Mes jeux, Diagnostic, Pilotes, Mesures, Paramètres), une fiche par jeu, 6 dialogues sur mesure, une icône dans la zone de notification et un dock flottant.
2. La base est solide. `Themes/Theme.xaml` est adopté partout (285 emplois de `Text.*`, environ 68 de `Card`). Les confirmations sont exemplaires (quoi, pourquoi, droits admin, redémarrage, « Annuler » par défaut). La microcopie est honnête et l'ingénierie mémoire et performance est très soignée.
3. Il n'y a pas de vrai système de design. On n'a ni jetons d'espacement, ni échelle typographique complète (97 `FontSize` posés à la main), ni variantes par gravité. Un même composant (bandeau, badge, jaquette, bouton icône) est recopié et dérive d'une vue à l'autre.
4. L'accessibilité est le point le plus faible. `TextMuted` (#6B7280) ne passe pas l'AA, alors qu'il porte environ 80 légendes informatives. Le focus clavier est invisible ou indéfini. Une vingtaine de boutons n'ont pas de nom UIA. Aucun libellé n'est relié à son champ, aucun titre n'est structuré, aucune région n'est annoncée (0 `LabeledBy`, 0 `HeadingLevel`, 0 `LiveSetting`, 0 `KeyBinding` dans l'appli).
5. Certaines actions sont impossibles au clavier : installer un jeu non installé, réordonner le dock et, probablement, cocher un élément dans les dialogues de liste.
6. Le retour d'état n'est pas fiable. Des erreurs graves ne passent que par une notification, qui peut être masquée sous AtlasOS (restauration ratée). Un contrôle en erreur peut aboutir à « Votre PC est prêt ». Les mesures automatiques n'apparaissent pas sans redémarrage. Les statuts sont verts quelle que soit leur gravité.
7. Côté cohérence, deux langages de dialogue cohabitent : 45 MessageBox Win32 « Oui / Non » et 6 fenêtres thémées. Le vert sert à la fois de marque, d'action, de sélection, de statut « OK » et de décor.
8. Côté architecture de l'information, la fiche du jeu mélange trois modèles de validation (immédiat, Enregistrer, Appliquer) et éclate l'éditeur de profil en trois endroits. La navigation n'affiche aucun signal d'état et ne propose pas de retour fiable.
9. Côté vocabulaire, un même concept porte plusieurs noms : session ou partie, optimisation, correction, réglage ou modification, mesure ou capture, plateforme, magasin, lanceur ou bibliothèque. « Annuler » veut dire tantôt « fermer », tantôt « défaire ».
10. La mise en page est fixe : 1240 × 860 au démarrage, barre latérale de 232 px sans mode compact, barres d'outils et colonnes à largeur fixe. Elle déborde dès 125 % d'échelle et à la largeur minimale (880).

---

## 2. Système de design actuel

### 2.1 Jetons présents (`Themes/Theme.xaml`)

| Famille | Contenu |
|---|---|
| Couleurs (18) | Window #0E1014, Sidebar #12151B, Card #181C23, CardHover #1F242D, Control #232833, ControlHover #2B313D, Border #262C36, Text #EDEFF3, TextSecondary #A2A9B4, TextMuted #6B7280, Accent #2FD27A, AccentHover #4BE391, AccentPressed #22B566, OnAccent #06140C, Ok #2FD27A, Warning #F5A524, Danger #F05252, Info #60A5FA |
| Brosses | `Brush.*` pour chaque couleur ; fonds « Soft » : Ok/Warning/Info 0,14, Danger 0,16, Accent 0,12 ; `Brush.CoverButton` #CC0E1014, `CoverButtonHover` #F0181C23, `CoverButtonBorder` #33FFFFFF |
| Polices | `Font.Display` (Segoe UI Variable Display), `Font.Icons` (Segoe Fluent Icons), `Font.Mono` (Cascadia Mono), déclarée à part, l.106 |
| Rayons | `Radius.Control` 8, `Radius.Card` 12. Rien d'autre. |
| Textes | PageTitle 28 SemiBold, SectionTitle 16 SemiBold, Label 13 SemiBold avec **marge intégrée** (0,14,0,6), Body 14, Secondary 14, Caption 12 (TextMuted), Mono 12, Status (accent, replié si vide), Icon 16 |
| Composants | Card (padding 20), Tile, Segment, Expander.Row, Banner (rayon, padding et bordure seulement), ButtonTemplate, Button.Base/Primary/Secondary/Ghost/Danger/SecondarySmall, NavButton, Badge (forme seule), Logo.Image / Logo.Mark |
| Accent Fluent | `App.xaml:19-49` : environ 25 valeurs hexadécimales recopiées (Accent*, SystemColors.AccentColor*Key) |

### 2.2 Manques

- **Espacements** : aucun jeton `Thickness`/`Double`. On relève les marges 32,24 (pages), 24,16 (bandeaux), 20, 16,12, 18,13, 16,7, 12,10, 10,6, 10,4, ainsi que des retraits manuels de 28 et 56 (SettingsView).
- **Typographie** : il manque Subtitle/Title 20–24, TitleLarge 36–40 (héros), Display 34–44 (chiffres et initiales), BodyStrong, Badge 12 SemiBold, Overline (capitales par style), DialogTitle 18–20, Stat 24. Résultat : 97 `FontSize` locaux (11 ×7, 12 ×8, 13 ×12, 14 ×17, 15 ×3, 16 ×4, 18 ×10, 20, 24 ×5, 28, 34 ×2, 36, 44 ×2, 48).
- **Rayons** : il manque Small 4 (aligné sur Fluent), Cover 10, Pill. On compte 19 littéraux (10 ×8, 14 ×4, 16 ×2, 20, 24, 32, 6, « 12,12,0,0 »).
- **États** : il n'y a aucun état de focus clavier dans les gabarits maison, et pas d'`IsPressed` sur Ghost ni sur Danger.
- **Composants absents** : InfoBar/Banner par gravité (Info, Success, Warning, Error) avec icône, action et fermeture ; Badge par gravité et Badge.Overlay ; Button.Icon, Button.Overlay, Button.PrimarySmall, Button.Link, Button.DangerFilled, Button.Large ; ToggleSwitch maison (ou Fluent) ; IconButton (glyphe et libellé, nom UIA automatique) ; FontIcon décoratif masqué de l'UIA ; style ToolTip ; ProgressBar.Thin ; « ligne de réglage » (titre, description, contrôle) ; « champ avec libellé » ; « liste à cocher » ; gabarit de dialogue (en-tête, corps défilant, pied de boutons) ; CoverTile.
- **Couleurs dédiées** : séries de graphe (Series1/2), scrim de bannière, remplaçant de jaquette, plateau et bord du dock, `Focus.Ring`.

### 2.3 Doublons et incohérences

- `Color.Accent` = `Color.Ok` (#2FD27A, l.20 et l.24). Il y a 31 emplois de `Brush.Accent` hors du thème, pour l'action, la sélection, le succès, l'état et le décor.
- `App.xaml` recopie l'accent environ 25 fois, au lieu de référencer `Color.Accent`/`AccentHover`/`AccentPressed`/`OnAccent`.
- Les géométries du logo sont recopiées dans `Logo.Image` et `Logo.Mark` (Theme.xaml:75-84 et 93-102).
- `BooleanToVisibilityConverter` est déclaré 9 fois sous 2 clés (`BoolToVisibility` ×8, `TrayBoolToVisibility`).
- `FrametimeChart.cs:23-27` recopie Info, Warning, Border et TextMuted, plus « Segoe UI » en 11 px.
- On trouve 3 dégradés « sans jaquette » (LibraryView.xaml:32 et 427, GamePageView.xaml:39, DockWindow.xaml:60) et 2 gabarits de jaquette divergents (LibraryView.xaml:16-208 et 420-470).
- Les libellés de badges « admin · redémarrage » existent deux fois (DiagnosticViewModel.cs:389 et ConfirmChangesDialog.xaml.cs:21), et le calcul des initiales quatre fois.
- Le menu contextuel d'un jeu existe deux fois (LibraryView.xaml:380-403 en XAML, DockWindow.xaml.cs:691 en C#).
- `Brush.CardHover`, `Brush.ControlHover` et `Brush.AccentPressed` ne servent qu'à l'intérieur du thème. `PathToImageConverter` est déclaré mais inutilisé dans LibraryView.xaml:13, et sa documentation est périmée.
- Les contrôles Fluent standard (rayon 4, hauteur d'environ 32) côtoient des boutons maison (rayon 8, hauteur d'environ 36), par exemple dans la rangée de filtres de « Mes jeux ».
- `Cursor=Hand` est posé sur les boutons, les segments, la navigation et Expander.Row (Theme.xaml:179, 215, 280, 350), contrairement à l'usage Windows.

### 2.4 Valeurs codées en dur (contraire à la règle « jamais de couleur codée en dur dans les vues »)

- **24 couleurs hexadécimales dans les vues** : LibraryView 9 (l.32-33, 88, 137, 141, 145, 427-428, 441), GamePageView 7 (l.39-40, 54-56, 62, 76), DockWindow.xaml 8 (l.16, 28, 36, 60-61, 72, 89).
- **Couleurs en C#** : 4 dans `FrametimeChart.cs:23-26` et 2 dans `DockWindow.xaml.cs:152-153`, plus l'onde du dock (l.651). `Brushes.Gray` dans `StatusToBrushConverter.cs:25`.
- **Doublons de jetons** : #CC0E1014 = `Brush.CoverButton`, #F0181C23 = `CoverButtonHover`, #33FFFFFF = `CoverButtonBorder`.
- **À garder tel quel** : `#01000000` (DockWindow.xaml:28). L'alpha de 1 est fonctionnel : il fait recevoir la souris.
- **Dimensions figées** : fenêtre 1240×860, minimum 880×560, barre latérale 232 ; jaquette 198×264, couplée à `ImageLoader.GridCoverWidth = 198` ; colonnes de la liste des captures (150/100/115/80/55/65 = 565 px) ; titres des lignes repliables 210 (GamePage) et 280 (Diagnostic) ; bannière à 320 de hauteur fixe.
- **Logique liée aux libellés** : `SortOptions` est comparé par chaîne (LibraryViewModel.cs:144-160). Les seuils de note 75/50 et les chaînes « Good »/« Fair »/« Poor » sont dupliqués (LibraryViewModel.cs:1217, GamePageViewModel.cs:91).

### 2.5 Contrastes calculés

**Texte** (seuil AA : 4,5:1 pour le texte normal, 3:1 pour le grand texte ≥ 18,66 px gras)

| Premier plan ↓ / fond → | Window | Sidebar | Card | CardHover | Control | ControlHover | AccentSoft/Sidebar¹ | WarningSoft/Window² | DangerSoft/Card³ |
|---|---|---|---|---|---|---|---|---|---|
| Text #EDEFF3 | 16,54 | 15,88 | 14,84 | 13,53 | 12,82 | 11,33 | 12,84 | 13,10 | 12,28 |
| TextSecondary #A2A9B4 | 8,04 | 7,72 | 7,22 | 6,58 | 6,23 | 5,51 | 6,25 | 6,37 | 5,97 |
| **TextMuted #6B7280** | **3,94** | **3,78** | **3,53** | **3,22** | **3,05** | **2,70** | **3,06** | **3,12** | **2,92** |
| Accent #2FD27A | 9,63 | 9,24 | 8,64 | 7,88 | 7,46 | 6,60 | 7,48 | 7,63 | 7,15 |
| Warning #F5A524 | 9,33 | 8,96 | 8,37 | 7,63 | 7,23 | 6,39 | 7,25 | 7,39 | 6,93 |
| **Danger #F05252** | 5,47 | 5,25 | 4,91 | **4,47** | **4,24** | **3,75** | **4,25** | **4,33** | **4,06** |
| Info #60A5FA | 7,49 | 7,19 | 6,72 | 6,13 | 5,81 | 5,13 | 5,82 | 5,93 | 5,56 |
| *Proposé : TextMuted #9099A6* | 6,61 | 6,35 | 5,93 | 5,41 | 5,13 | 4,53 | 5,14 | 5,24 | 4,91 |
| *Proposé : Danger #F47272* | 6,80 | 6,53 | 6,10 | 5,56 | 5,27 | 4,66 | 5,28 | 5,38 | 5,05 |

¹ carte « Session en cours » ; ² bandeaux d'avertissement ; ³ survol de Button.Danger.

**Autres paires**

| Paire | Ratio | Constat |
|---|---|---|
| OnAccent sur Accent (bouton principal) | 9,54 | OK |
| Bouton Primary désactivé (opacité 0,4 sur Card) | 2,56 | sert à afficher un état (« Optimisations activées », « En cours ») : échec |
| Bouton Secondary désactivé | 3,26 | échec |
| Badges « admin · redémarrage » à opacité 0,7 sur Button.Danger | 3,00 | échec (DiagnosticView.xaml:61) |
| Border contre Card / contre Window | 1,22 / 1,36 | contour quasi invisible (WCAG 1.4.11 : 3:1) |
| Fond de Button.Secondary contre Card | 1,16 | le bouton se lit comme du texte en gras |
| Segment : texte choisi (Accent) contre non choisi (TextSecondary) | 1,20 | sélection portée par la teinte seule |
| Vert Ok contre rouge Danger (écarts de comparaison) | 1,76 | indiscernables pour un deutéranope |
| Séries Info contre Warning (graphe) | 1,25 | idem |
| État vide du dock (TextMuted sur #121620) | 3,74 | 2,84 à 90 % d'opacité sur un fond d'écran blanc |
| Texte sur la bannière du jeu (fondu à 60 %, image claire) | ≈ 2,1–3,5 | estimation, GamePageView.xaml:54-56 et 90-92 |

---

## 3. Problèmes transverses priorisés

### 3.0 Contre-vérification des problèmes de sévérité « haute »

Les 44 signalements « haute » de l'audit se ramènent à 26 problèmes distincts, une fois les doublons regroupés (TextMuted a été signalé 4 fois, les boutons sans nom 3 fois, etc.). Tous ont été relus dans le code.

- **Confirmés tels quels**
  - NavButton : `FocusVisualStyle` à null.
  - Gabarits maison sans état de focus.
  - TextMuted.
  - Boutons à contenu `StackPanel` sans nom : `ContentControl.GetPlainText` renvoie null pour un Panel, donc le nom UIA est vide.
  - Cartes non installées accessibles au survol seulement.
  - Bandeau de détection en `StackPanel` horizontal : pas de retour à la ligne.
  - Erreurs critiques signalées uniquement par notification : App.xaml.cs:259, 270-275 ; SessionViewModel.cs:52-81.
  - `DiagnosticStatus.Error` rangé dans « À savoir » et ignoré par `OneClickOptimization` (Core, l.20 et 48).
  - Mesures automatiques jamais rafraîchies.
  - Pas d'état « aucun résultat » (`HasGames => Games.Count > 0`).
  - Colonnes de Mesures à 565 px.
  - Mieux / moins bien porté par la couleur seule.
  - MessageBox Win32 pour 45 appels.
  - « Annuler » ambigu.
  - Vocabulaire optimisation / correction.
  - Trois modèles de validation et éditeur éclaté sur la fiche du jeu.
  - Boutons de la bannière sans nom.
  - `ShowGame` sans garde des modifications non enregistrées (LibraryViewModel.cs:798-802).
  - Tronquage à 280 px dans le Diagnostic.
  - « Détails et explication » toujours affiché, explication placée après les détails.
  - Dock : bande sur `WorkArea`, minuteur non lancé dans `OnTriggerEnter`, `SlideOut` à l'activation, réordonnancement par glisser seulement (`Move` n'est appelé que par DockWindow.xaml.cs:561).
  - Bouton icône GamePageView.xaml:329, dont le nom est le caractère privé U+E711.
  - Champs non reliés à leur libellé.
- **Nuancés**
  1. *ConfirmChangeDialog.xaml:30, « fausse dans 2 usages sur 3 »* : **reclassé en moyenne**. La phrase n'est pas fausse : `AppliedFixes` (DiagnosticViewModel.cs:333-341) liste aussi les réglages `game.*` sous Diagnostic › Avancé. Elle est trompeuse (le bon endroit est la fiche du jeu) et parle d'un « onglet » qui n'existe pas.
  2. *Débordement de la barre d'outils de Mes jeux* : les largeurs fixes, sans retour à la ligne, sont confirmées. Le débordement à 1240 est **calculé** (≈ 980 contre ≈ 928 DIP disponibles). À confirmer par capture avant d'en faire un critère.
  3. *Informations en infobulle* : en WPF récent, l'infobulle d'un contrôle **focalisable** s'affiche au focus clavier (ComboBox « Priorité », TextBox « ex. chrome.exe »). Le défaut reste entier pour les éléments non focalisables : le TextBlock « Programmes à fermer » (avertissement de perte de données) et les pastilles en `Border` de la grille.
  4. *Listes à cocher des dialogues (GameScan, ProcessPicker)* : défaut déduit du comportement par défaut de `ListBox` (`TabNavigation=Once`, `IsTabStop=false`, rien de surchargé dans le code). **À valider au clavier** avant correction.
  5. *Bande du dock au-dessus de la barre des tâches* : vrai seulement si la barre occupe le même bord. C'est le cas par défaut (dock « En bas » et barre Windows 11 en bas).
- **Écartés** : aucun.

Légende : **[H]** haute, **[M]** moyenne, **[B]** basse.

### 3.1 Accessibilité

- **[H] Contraste de TextMuted.** Theme.xaml:19 et `Text.Caption` l.139-143. 80 lignes l'utilisent pour des informations utiles : aides des réglages, états de recherche, avertissement Windows Update (DriversView.xaml:195-196), statut de session (MainWindow.xaml:48), « Pourquoi » des confirmations groupées (ConfirmChangesDialog.xaml:35), en-têtes de section. Sont aussi concernés le texte indicatif de la recherche (LibraryView.xaml:222), les libellés du graphe (FrametimeChart.cs:135, 11 px) et le chevron d'Expander.Row. → Passer TextMuted à **#9099A6** (≥ 4,5 sur toutes les surfaces, voir 2.5), ou réserver TextMuted au décoratif.
- **[H] Focus clavier.** Il est supprimé sur la navigation (Theme.xaml:353). Aucun gabarit maison n'a d'état de focus (ButtonTemplate l.261-273, Segment l.176-198, Expander.Row l.219-236) : on hérite du pointillé système, invisible sur le sombre. Le focus d'une jaquette est identique à son survol (LibraryView.xaml:176 et 197). → Créer un `Focus.Ring` commun (2 px, décalage 2, rayon du contrôle + 2) et l'appliquer à tous les styles.
- **[H] Boutons sans nom UIA.** LibraryView.xaml:232, 250, 267 ; GamePageView.xaml:61, 95, 113, 121, 138, 144 ; DiagnosticView.xaml:15, 58, 91 ; DriversView.xaml:17, 63, 130 ; MeasuresView.xaml:66, 102 ; SettingsView.xaml:143. Les libellés variables (« Actualisation… », « Jouer »/« En cours », « Épingler »/« Retirer du dock ») ne sont pas exposés non plus. Bouton icône seul : GamePageView.xaml:329. → Créer un contrôle `IconButton` (Glyph + Label) qui alimente le nom accessible.
- **[H] Libellés non reliés aux champs** (0 `LabeledBy` dans l'appli) : GamePageView.xaml:312, 317, 344, 381, 385, 396, 402, 590 ; MeasuresView.xaml:46-62 ; SettingsView.xaml:68, 72, 151, 153 ; IgdbSearchDialog.xaml:15 ; GameScanDialog.xaml:19, 26. → `Label Target` (ce qui donne aussi une touche d'accès) ou `LabeledBy`.
- **[H] Actions réservées au survol.** Pour les jeux non installés (LibraryView.xaml:421-470), la carte est un `Grid` non focalisable sans menu contextuel : installer au clavier est **impossible**. Pour les jeux installés (l.175-203), le menu contextuel (Maj+F10) existe mais rien ne le signale. Les boutons sont imbriqués dans le gabarit d'un `Button`.
- **[H] Dock.** Le réordonnancement se fait uniquement par glisser (WCAG 2.2 2.5.7, DockWindow.xaml.cs:561). Les cellules sont des `Grid` sans rôle « bouton » ni Invoke (DockWindow.xaml:48-54). `DockItemViewModel` n'a pas de `ToString()`. → Ajouter « Déplacer vers la gauche / la droite » au menu, plus une liste ordonnable dans la fenêtre principale, et un `AutomationPeer` avec `IInvokeProvider`.
- **[H] Avertissement critique dans une infobulle non focalisable** : GamePageView.xaml:322-323 (arrêt forcé après 5 s, perte de données) ; pastilles « Désinstallé », « Disque absent » et note (LibraryView.xaml:89, 141, 145).
- **[H, à valider] Listes à cocher des dialogues inaccessibles au clavier** : GameScanDialog.xaml:15-27, ProcessPickerDialog.xaml:14-25.
- **[M] Couleur seule.** Écarts de comparaison (MeasuresView.xaml:207-216) : pour P99, « −3 % » est un progrès. Séries du graphe (FrametimeChart.cs:23-24). Point de statut de 9 px du Diagnostic (DiagnosticView.xaml:76), qui ne distingue pas Info d'Erreur. Sélection du Segment (Theme.xaml:190-193). Pastille « en cours » du dock (DockWindow.xaml:75).
- **[M] Rien n'est annoncé** (0 `LiveSetting`) : `Text.Status`, LaunchStatus/ArtworkStatus (LibraryView.xaml:297-298), CaptureStatus (MeasuresView.xaml:76), Updates.StatusText et IgdbStatus (SettingsView.xaml:41, 162), ProgressText (DriversView.xaml:79), InputError (GamePageView.xaml:437). Aucun titre structuré (0 `HeadingLevel`). Changer de page n'annonce rien et laisse le focus dans la barre latérale.
- **[M] Ordre de tabulation contraire à l'ordre visuel**, causé par les éléments `DockPanel.Dock` Right/Bottom déclarés en premier : GamePageView.xaml:18 (barre Enregistrer d'abord), 329-332, 340-345 ; LibraryView.xaml:282 ; DiagnosticView.xaml:91 ; SettingsView.xaml:200-202 ; tous les dialogues. Aucun focus initial (0 `FocusManager.FocusedElement`).
- **[M] Éléments de liste nommés d'après leur type .NET** (repli sur `ToString`) : IgdbSearchDialog.xaml.cs:75, GameScanDialog, ProcessPickerDialog, MeasuresView.xaml:113.
- **[M] Danger insuffisant** sur Control, CardHover et DangerSoft. État transmis par un bouton désactivé à 40 % (DiagnosticViewModel.cs:318, GamePageView.xaml:103).
- **[B]** Textes de 11 px (LibraryView.xaml:135-147 et 442, ProcessPickerDialog.xaml:23). Glyphes exposés comme texte (`Text.Icon`). Animations qui ignorent `SystemParameters.ClientAreaAnimation` (LibraryView.xaml:180, dock). Cibles d'environ 26-27 px (`SecondarySmall`, GamePageView.xaml:329). Focus non rendu à la jaquette au retour de la fiche (LibraryViewModel.cs:894). Aucun équivalent textuel du graphe.

### 3.2 Retour d'état

- **[H] Erreurs graves uniquement en notification**, alors que CLAUDE.md exige un affichage aussi dans la fenêtre : restauration impossible au démarrage (App.xaml.cs:270-275), erreur de détection en cours de route (l.259), erreur d'interface avec message brut (l.57-61), échecs de restauration et avertissements en fin ou en début de partie, reprise après plantage (SessionViewModel.cs:52-81). L'utilisateur peut garder des réglages modifiés sans le savoir, ce qui contredit le principe 1. → Ajouter un état persistant (liste d'alertes de la coque) affiché en InfoBar Erreur, avec « Réessayer » et « Ouvrir le journal ».
- **[H] Diagnostic.** Un contrôle `Error` est classé « À savoir » (DiagnosticViewModel.cs:217-222) et ignoré par `OneClickOptimization` : la vue Simple peut afficher « Votre PC est prêt » en vert, et les compteurs ne tombent pas juste (l.313). → Créer un état « Non vérifié » distinct, compté, avec « Réessayer ».
- **[H] Les mesures automatiques n'apparaissent pas dans Mesures** avant un redémarrage (MeasuresViewModel.cs:51-53 ; `CaptureAdded` n'est écouté que par LibraryViewModel.cs:80). Une capture élaguée reste listée avec un graphe vide et sans message.
- **[H] Mes jeux sans état « aucun résultat »** (LibraryView.xaml:355, LibraryViewModel.cs:608). → Afficher « Aucun jeu ne correspond à « … » » avec un bouton « Effacer les filtres », et « 3 sur 42 jeux » dans le sous-titre.
- **[H] Dock**
  - Il s'active sans rien montrer (SlideOut immédiat, DockWindow.xaml.cs:213, avec les réglages par défaut DockEnabled=false et AutoHide=true), sans aperçu lors des réglages.
  - Il reste affiché si la souris traverse la bande sans toucher le plateau (l.301).
  - La bande fait 2 px et se trouve **au-dessus** de la barre des tâches (`WorkArea`, l.166 et 208) ; elle capte aussi les clics des 2 px du bas des fenêtres.
- **[H] Modifications non enregistrées perdues** quand on ouvre un autre jeu depuis le dock (LibraryViewModel.cs:798-802) ou quand on quitte depuis la zone de notification. Seul le bouton « Mes jeux » les protège (l.887-893).
- **[M] Gravité ignorée**
  - `Text.Status` est toujours vert (Theme.xaml:153). « 1 jeu n'est plus installé » s'affiche en vert (LibraryViewModel.cs:1067), tout comme « Capture échouée » (MeasuresView.xaml:76).
  - Le bandeau de mise à jour reste vert en cas d'échec (MainWindow.xaml:84-98, UpdateService.cs:120-125). Il n'a pas de barre de progression, alors que `DownloadPercent` existe, ni de fermeture. « OK » sert de fermeture sur l'autre bandeau.
- **[M] Erreurs brutes** `ex.Message`, souvent en anglais ou en HRESULT : LibraryViewModel.cs:735 et 1005, SettingsViewModel.cs:214 et 265, MeasuresViewModel.cs:161, IgdbSearchDialog.xaml.cs:58, DriversViewModel.cs:117 et 139. Cibles techniques (`registry:…`) dans DiagnosticViewModel.cs:140.
- **[M] Aucun indicateur d'activité** : analyse, recherche Windows Update de 25-30 s, lecture du pilote. La carte héros est orange avant tout résultat (DiagnosticViewModel.cs:53-55). Les libellés de bouton qui changent (« Actualisation… ») font varier leur largeur.
- **[M] Bilans manquants ou bloquants.** Une correction réussie disparaît dans « OK », repliée. Un échec partiel masque le rappel de redémarrage (DiagnosticViewModel.cs:106-113). Pas d'état « redémarrage en attente » persistant. Des succès s'affichent en MessageBox modale (l.112, 264). « Enregistrer » reconstruit toute la fiche (LibraryViewModel.cs:909) sans confirmation. Aucun bilan après la détection des jeux ni après l'installation d'un pilote.
- **[M] Dock**
  - Aucun retour au lancement d'un jeu ; double lancement possible (DockWindow.xaml.cs:516).
  - Chaque écriture de settings.json le replie (DockController.cs:29 → `ApplySettings` → `SlideOut`).
  - Un jeu désinstallé paraît jouable.
  - Épingler un jeu alors que le dock est désactivé ne produit aucun effet (LibraryViewModel.cs:648).
- **[M]** Fermer la fenêtre la masque sans explication (MainWindow.xaml.cs:45). « Quitter » n'existe que dans la zone de notification.
- **[M] États vides manquants** : GameScanDialog s'ouvre même sans jeu trouvé (LibraryViewModel.cs:767) ; liste des captures, dossiers de jeux, programmes à fermer ; sections « À savoir (0) » et « OK (0) ».
- **[M] « Ignorer » un nouveau jeu Steam est définitif**, sans annulation (NewSteamGamesViewModel.cs:128). « Supprimer les identifiants » IGDB se fait sans confirmation (SettingsViewModel.cs:224). « Terminer et restaurer » se fait sans explication (MainWindow.xaml:49-50).
- **[B]** `IsDirty` ne revient jamais à faux (ProfileEditorViewModel.cs:221-225). Disque illisible = « Lecture… » sans fin (GamePageViewModel.cs:177-181). « Pas encore de note… » s'affiche pendant le calcul (GamePageView.xaml:291). Tuile et liste des mesures figées (GamePageViewModel.cs:129-170).

### 3.3 Architecture de l'information

- **[H] Fiche du jeu : trois modèles de validation sans repère.** « Mon réglage » est enregistré immédiatement (GamePageViewModel.cs:62). Le profil passe par la barre « Enregistrer » (GamePageView.xaml:17-30). Pilote NVIDIA et Graphismes ont leur propre « Appliquer » avec confirmation (l.431, 473). Le commentaire de la l.155-156 le reconnaît.
- **[H] Éditeur de profil éclaté** : carte « Pendant la partie » (l.296-361), ligne « Lancement » (l.367-411), ligne « Profil » (l.579-598, après Disque, Mesures et Temps de jeu). Une modification peut se trouver dans une section repliée quand la barre « non enregistrées » apparaît. → Créer une zone unique « Réglages de partie » liée à la barre, et une zone « Réglages permanents (Windows et pilote, appliqués tout de suite, annulables) ».
- **[M] Fiche trop dense** : bannière de 320 px avec 5 actions de même poids, 4 tuiles qui répètent les lignes, 2 cartes, 7 lignes repliables. « Retirer ce jeu » est enfoui (l.595). « Mesurer les FPS » quitte la fiche sans le dire (LibraryViewModel.cs:819-823).
- **[M] Navigation.**
  - Cliquer « Mes jeux » déjà sélectionné ne ferme pas la fiche (MainViewModel.cs:82-85).
  - Il n'y a pas de fil d'Ariane.
  - Les entrées n'ont ni compteur ni signal : `NavItem` ne porte aucun badge (« 3 à corriger », pilote disponible, mise à jour prête).
  - « Paramètres » est mêlé aux pages de travail.
- **[M] Réglages par jeu gérés à deux endroits** (fiche du jeu et Diagnostic › Avancé › « Corrections appliquées », DiagnosticViewModel.cs:333-341), avec des compteurs divergents entre les vues Simple et Avancé.
- **[M] Regroupements maladroits**
  - Mes jeux : trois boutons voisins aux sens proches (Actualiser, Rechercher des jeux installés, Ajouter un jeu), recherche et tri sur la ligne 1, filtres sur la ligne 2, case « non installés » perdue à droite.
  - Paramètres : 8 cartes dans un ordre peu logique (SettingsView.xaml:19-196). « Note des jeux » ne contient que la mesure automatique.
  - Le chemin de PresentMon est sur Mesures (MeasuresView.xaml:77-80) et non dans Paramètres.
- **[M] Mise en page non adaptative**
  - Fenêtre de 1240×860 au démarrage, plus grande que la zone de travail à 125 % (≈ 826 de hauteur) et à 150 % (≈ 688). La position n'est pas gardée d'un lancement à l'autre.
  - Barre latérale de 232 px sans mode compact.
  - Barre d'outils de Mes jeux ≈ 980 DIP (calcul).
  - Tuiles en 4 colonnes et cartes en 2 colonnes fixes (GamePageView.xaml:159-168, 234-239). Bannière de hauteur fixe.
  - Mesures : grille fixe sans défilement (MeasuresView.xaml:88-96), colonnes de 565 px pour environ 470 disponibles.
  - Bandeaux globaux empilés hors défilement, avec 24 px de gouttière contre 32 pour les pages (MainWindow.xaml:73, 84, 101).
- **[B]** Section des non installés peu distinguée (titre en capitales de 12 px gris, LibraryViewModel.cs:341). Deux comptes différents (case et en-tête).

### 3.4 Cohérence

- **[H] Deux langages de dialogue.** `DialogService.Confirm/ConfirmUndo/ShowInfo/ShowError` utilisent la MessageBox Win32 (DialogService.cs:69-78 et 135-138) : 8 Confirm, 3 ConfirmUndo, 11 ShowInfo, 23 ShowError, plus App.xaml.cs:310. Elle est claire, non thémée, avec « Oui / Non », l'icône « ? » et le titre « OptiGame ». À côté, 6 dialogues sombres soignés. Activer passe par un dialogue riche, désactiver ou restaurer par une MessageBox (DiagnosticViewModel.cs:83 contre 124 et 271).
- **[M] Couleurs sémantiques détournées.** Accent = Ok. Warning colore la série « Après » (MeasuresView.xaml:190, FrametimeChart.cs:24). Danger colore l'en-tête « Options avancées » (DiagnosticView.xaml:53). L'accent sert aux icônes décoratives et aux valeurs neutres (FPS : GamePageView.xaml:205 et 536). Les bandeaux « disponible » (AccentSoft) et « installée » (OkSoft) sont presque identiques.
- **[M] Bandeaux recopiés**, car `Banner` n'est qu'une coquille : MainWindow.xaml:73-115, LibraryView.xaml:308-350, MeasuresView.xaml:28 (sans icône), DiagnosticView.xaml:199, ConfirmChangeDialog.xaml:18, DriverInstallDialog.xaml:19. Mises en page, ordres d'actions et fermetures diffèrent. Le bandeau de détection ne passe pas à la ligne (MainWindow.xaml:77-80). → Créer un composant `InfoBar` sur le modèle WinUI.
- **[M] Composants refaits localement** : boutons ronds de jaquette (LibraryView.xaml:46-118), bouton retour (GamePageView.xaml:62), badges (couleur et typographie libres), deux gabarits de jaquette divergents (remplaçant, zoom, liseré 2 px ou 1 px, infobulle, clic), deux mécanismes de couleur d'état (DataTrigger dans Pilotes, `StatusToBrushConverter` dans Diagnostic), duplication des cartes GPU et chipset (DriversView.xaml:39-92 et 108-159).
- **[M] Retour arrière différent** pour deux réglages permanents voisins : NVIDIA a un bouton « Annuler » (GamePageView.xaml:433) ; Graphismes demande de choisir « Laisser Windows décider » puis « Appliquer » (GameGraphicsViewModel.cs:84).
- **[M] Trois dessins d'Expander** imbriqués : Expander.Row maison, Fluent gris et Fluent rouge (DiagnosticView.xaml:39, 53, 73 ; GamePageView.xaml:273, 349).
- **[M] Trop de boutons principaux** à l'écran (Mes jeux : barre d'outils, chaque bandeau de nouveau jeu, chaque carte non installée ; Diagnostic Avancé : un bouton vert par correction). Le bouton par défaut varie d'un dialogue à l'autre (Annuler dans les confirmations, l'action principale dans les sélecteurs, « Rechercher » dans IGDB) alors que le bouton vert a toujours le même aspect.
- **[B]** Cinq formats de date (DriversViewModel.cs:383 et 449, DiagnosticViewModel.cs:231 et 404, MeasuresViewModel.cs:124, GamePageViewModel.cs:212). Glyphes en caractères privés bruts dans le C# (MainViewModel.cs:30-34). E896 « téléchargement » désigne à la fois Pilotes, la mise à jour d'OptiGame et un lien de navigation. Initiales et rayon du dock fixes (14 px, 18 px) quelle que soit la taille des icônes. Le style d'infobulle du dock diffère de celui de l'appli.

### 3.5 Vocabulaire

- **[H] « Annuler » a deux sens** : fermer sans rien faire (tous les dialogues, capture) et défaire un réglage (DiagnosticView.xaml:227, GamePageView.xaml:433, DialogService.cs:70 « Annuler « X » ? » avec Oui/Non : triple négation). À cela s'ajoutent « Annuler les modifications » (GamePageView.xaml:22) et « Désactiver les optimisations » (DiagnosticViewModel.cs:59), qui désignent la même restauration.
- **[H] Un même objet journalisé porte quatre noms** : optimisation (vue Simple), correction (vue Avancé, DiagnosticView.xaml:217), réglage (DiagnosticViewModel.cs:299 et 323) et modification (ConfirmChangeDialog.xaml:4). Les vérifications s'appellent « contrôles » ou « points ».
- **[M] Session / partie / En cours / Session en cours** (MainWindow.xaml:46-50, App.xaml:64, GamePageView.xaml:87 et 104, LibraryView.xaml:135, SessionViewModel.cs:89). « Session en cours » est répété deux fois de suite dans la carte. La commande de fin porte deux libellés.
- **[M] « Profil » a trois sens** : le jeu ajouté (« Créer les profils cochés », GameScanDialog.xaml:12, « Profil désactivé »), les réglages de partie (« Ce que fera ce profil ») et le profil du pilote NVIDIA (FrameCapViewModel.cs:69-70). Ailleurs, l'interface parle d'« Ajouter à Mes jeux ».
- **[M] Autres doublets.**
  - Mesure / capture.
  - Plateforme / magasin / lanceur / bibliothèque (LibraryView.xaml:284-289 et 412).
  - « Page du jeu » désigne à la fois la fiche OptiGame et la page Steam.
  - « Onglet » est employé alors que l'appli n'a pas d'onglets (ConfirmChangeDialog.xaml:30, ConfirmChangesDialog.xaml:10, GameScanDialog.xaml.cs:16).
  - « Filtrer » sert au champ de recherche (LibraryView.xaml:223) alors que « Rechercher » désigne une analyse du disque qui ajoute des jeux (l.256).
  - Verbes en vrac : Activer, Appliquer, Confirmer dans un même parcours (DiagnosticViewModel.cs:318, ConfirmChangesDialog.xaml.cs:17).
- **[M] Libellés différents pour une même action** : Actualiser, Relancer l'analyse, Rechercher à nouveau, Rechercher maintenant ; Nouveautés et Voir les nouveautés ; Parcourir…, Changer…, Choisir PresentMon…, Changer d'exécutable… ; Retirer de Mes jeux… et Retirer ce jeu. Les points de suspension manquent sur les commandes qui ouvrent un dialogue (« Ajouter un jeu », « Changer la jaquette », « Supprimer », « Appliquer », corrections).
- **[M] Jargon et densité** : 1 % low, Frametime P99, appid, Client ID/Secret, « admin », fixes.json, « chaîne d'affichage principale ». Aides jusqu'à 429 caractères (SettingsView.xaml:40 ; aussi l.116, 127, 23 ; MeasuresView.xaml:24 et 233 ; GameScanDialog.xaml:9). « Ce qui change » est un chemin de registre en police mono (ConfirmChangeDialog.xaml:12).
- **[M] Textes inexacts**
  - « Steam et vos dossiers de jeux » alors qu'Epic et GOG sont couverts (LibraryView.xaml:359, GameScanDialog.xaml.cs:16, SettingsView.xaml:173).
  - Recherche « auprès de NVIDIA » sur un PC AMD (DriversViewModel.cs:87, DriversView.xaml:96).
  - « Optimisations activées » affiché alors qu'OptiGame n'a rien appliqué (DiagnosticViewModel.cs:318).
  - « Installer » pour GOG, qui ouvre en fait Galaxy (LibraryViewModel.cs:1295).
- **[B] Typographie française**
  - Pluriels en « (s) » (DiagnosticViewModel.cs:232, DriversViewModel.cs:132, DiagnosticView.xaml:219, IgdbSearchDialog.xaml.cs:47).
  - Capitales tapées dans les chaînes (« À CORRIGER », « DANS VOS BIBLIOTHÈQUES… »).
  - Aucune espace insécable, ni U+00A0 ni U+202F.
  - « vue Avancé » au lieu de « vue avancée ».
  - « installer le 617.14 » sans nom.
  - « Il reste à faire de votre côté » : phrase incomplète.
  - Accord manquant : « cette capture et leur fichier ».

---

## 4. Par écran

### Coque (MainWindow, barre latérale, bandeaux, zone de notification)
- **Rôle** : naviguer entre les 5 pages, rappeler la partie en cours, afficher les alertes globales (détection, mises à jour). L'appli vit dans la zone de notification.
- **Actions** : naviguer ; « Terminer et restaurer » ; Installer maintenant, Nouveautés, OK ; menu de l'icône : Ouvrir (en gras), terminer la session, Quitter.
- **Problèmes clés** : focus invisible dans la navigation ; taille initiale fixe et position non mémorisée ; pas de mode compact ; pas de badges d'état ; bandeaux hétérogènes (détection tronquée, erreur de mise à jour en vert, pas de fermeture) ; erreurs critiques limitées aux notifications ; carte de session redondante (« Session en cours » ×2, nom du jeu en légende peu contrastée) ; icône de notification immuable (ni partie optimisée, ni panne) ; notifications non tronquées et sans action au clic ; masquage silencieux à la fermeture ; aucun raccourci clavier.
- **Pistes**
  - Barre latérale de type NavigationView : mode compact sous environ 1008 px, Paramètres en pied, badges textuels et accessibles (« 3 à corriger »), clic sur l'entrée active = retour à la racine.
  - InfoBar unique par gravité, alignée sur la gouttière des pages, avec regroupement (« 2 alertes ») et fermeture.
  - Carte de session : titre = nom du jeu, légende « Optimisé depuis 20:15 », action « Arrêter l'optimisation et restaurer » avec explication.
  - Taille initiale ≤ 90 % de la zone de travail, `RestoreBounds` et `WindowState` mémorisés dans settings.json.
  - Raccourcis : Ctrl+1…5, Ctrl+F, F5, Échap ou Alt+← (retour, avec la garde des modifications non enregistrées).
  - Variantes de l'icône de notification (partie optimisée, détection en panne), chargées par le même chemin `IconMetrics`.
  - Premier masquage expliqué une fois ; « Quitter OptiGame » accessible depuis la fenêtre.

### Mes jeux (grille)
- **Rôle** : bibliothèque des jeux suivis et des jeux possédés non installés (Steam, Epic, GOG) ; point d'entrée vers Jouer et la fiche du jeu.
- **Actions** : Ajouter un jeu, Rechercher des jeux installés, Actualiser, trier, filtrer (nom, magasin, genre, type), afficher les non installés ; sur une carte : ouvrir, Jouer, Épingler, Retirer (survol et menu) ; sur une carte non installée : Installer, Page Steam (survol) ; bandeaux des nouveaux jeux Steam et des jeux désinstallés.
- **Problèmes clés** :
  - Barre d'outils qui déborde.
  - Trois boutons d'ajout ou de relecture concurrents, sans nom UIA.
  - Cartes non installées inutilisables au clavier ; actions des jeux installés au survol seulement.
  - Ni état « aucun résultat », ni état vide utile (aucun bouton, superposé aux non installés).
  - Statuts insérés dans le flux, qui décalent la grille et disparaissent au bout de 12 s, en vert même pour un avertissement.
  - Bandeaux empilés sans limite. « Ignorer » est définitif. Le bandeau « désinstallés » ne peut être ni fermé ni filtré.
  - Opacité 0,5 qui ternit aussi les pastilles et les boutons (contraste de la pastille « Désinstallé » ≈ 3,1).
  - Deux gabarits de jaquette ; jaquettes Steam locales non utilisées pour les jeux installés sans IGDB.
  - Épingle visible alors que le dock est éteint par défaut.
  - Tri sans libellé, qui ne s'applique pas aux non installés.
- **Pistes** :
  - Ligne 1 : titre et bouton fractionné « Ajouter des jeux ▾ » (« Détecter les jeux installés… », « Choisir un fichier .exe… »), Actualiser en bouton icône (F5).
  - Ligne 2 : recherche extensible avec loupe et ×, tri libellé « Trier par », filtres repliables (« Filtres (n) ») dans un WrapPanel.
  - Contrôle `CoverTile` unique (installé ou non installé), focalisable, actions visibles aussi sur `IsKeyboardFocusWithin`, menu contextuel complet, statut UIA (`ItemStatus` : en cours, désinstallé, note, épinglé), actions désactivées selon l'état avec la raison.
  - InfoBar à emplacement fixe ; bandeau groupé « 3 nouveaux jeux Steam ».
  - En-têtes de section « Installés · 42 » et « Non installés · 271 ».
  - Navigation aux flèches dans la grille (`TabNavigation=Once`).

### Fiche du jeu (GamePageView)
- **Rôle** : identité du jeu, lancement, note sur ce PC, réglages appliqués pendant la partie, réglages permanents (NVIDIA, carte graphique), informations (disque, mesures, temps de jeu).
- **Actions** : Jouer ; Page Steam ; Épingler ; Changer la jaquette ; Mesurer les FPS ; Mon réglage ; éditeur (activation, plan, priorité, programmes, lancement, nom, exe) avec Enregistrer ou Annuler les modifications ; plafond de FPS et carte graphique avec Appliquer ou Annuler ; Retirer ce jeu.
- **Problèmes clés** :
  - Trois modèles de validation et un éditeur éclaté (3.3).
  - Boutons de la bannière et champs sans nom accessible ; avertissements dans des infobulles.
  - Bannière de hauteur fixe qui déborde en fenêtre étroite ; texte sur fondu peu contrasté ; chemin de l'exe en position principale, qui suit la saisie non enregistrée.
  - Tuiles redondantes et non cliquables.
  - « Jouer » devient « En cours » désactivé (2,56:1) au lieu d'offrir une fin de partie.
  - Désactivation du profil sans effet visible ; « Ce que fera ce profil » replié par défaut.
  - Retour arrière NVIDIA et Graphismes incohérent ; champ de plafond prérempli avec la valeur conseillée.
  - Valeur brute Auto HDR (2097) affichée.
  - États de chargement infinis (disque) et faux état vide (note).
  - Dialogues Oui/Non au lieu de « Enregistrer et jouer ».
  - Pas de Ctrl+S, Échap ni Alt+←.
- **Pistes** :
  - Fil d'Ariane « Mes jeux › Nom » hors de l'image.
  - Bannière : Jouer, 1 ou 2 actions, et un menu « … » (Page Steam, Épingler, Changer la jaquette…, Changer d'exécutable…, Retirer de Mes jeux…).
  - Pendant la partie : « Partie en cours » et « Arrêter l'optimisation et restaurer… ».
  - Zone « Réglages de partie » (interrupteur « Optimiser ce jeu pendant les parties », réglages grisés quand il est désactivé, résumé toujours visible, barre Enregistrer).
  - Zone « Réglages permanents » (même motif « Réglé par OptiGame le … » et « Restaurer l'original… »).
  - Zone « Informations » (onglets ou sections : Vue d'ensemble, Réglages, Historique) ; tuiles actionnables ou supprimées.
  - Validation en ligne ; `IsDirty` calculé par comparaison ; avertissements visibles en ligne.
  - Images de la bannière décodées hors du thread UI, comme la grille.

### Diagnostic (vues Simple et Avancé)
- **Rôle** : analyse en lecture seule ; application et restauration des optimisations journalisées (fixes.json).
- **Actions** : Activer les optimisations (n), Désactiver les optimisations (n), Relancer l'analyse, Simple ou Avancé ; en vue Avancé : appliquer une correction, une option avancée (double confirmation), annuler une correction ; lien vers Pilotes.
- **Problèmes clés** :
  - Contrôle en erreur invisible (« prêt » faux).
  - Boutons de correction sans nom.
  - Titres et résumés tronqués (280 px, résumé jamais répété une fois déplié).
  - Actions manuelles cachées derrière deux dépliages, l'explication après des mesures en police mono.
  - Bouton désactivé utilisé comme état ; paire Activer / Désactiver qui ressemble à un interrupteur.
  - Vocabulaire optimisation / correction / Annuler.
  - Corrections actives listées « annulables » sans action en vue Simple ; 4 étapes pour en restaurer une.
  - `game.*` mêlé aux corrections du PC.
  - Retour de succès absent ou modal ; pas d'état « redémarrage en attente ».
  - Titres de sections en capitales tapées ; triple codage du statut ; trois styles d'Expander ; ordre de tabulation inversé ; chargement non signalé (pastille orange avant résultat).
- **Pistes** :
  - Verdict qui n'est coloré qu'après les résultats, avec un état de chargement neutre.
  - Action principale unique « Appliquer les 3 optimisations… » ; plus bas, « Optimisations actives » avec « Restaurer l'original » par ligne et « Tout restaurer… ».
  - Statut = icône de forme et libellé (OK, À corriger, À savoir, Non vérifié) ; nom UIA « Titre — statut : résumé ».
  - Actions manuelles en cartes dépliées (« Comment faire » d'abord, « Valeurs mesurées » repliées).
  - InfoBar de résultat (« 3 appliquées, 1 en échec ») et de redémarrage (« Redémarrer maintenant », « Plus tard »).
  - Dépliage conservé d'une analyse à l'autre (clé = CheckId).
  - Badges « Droits administrateur » (bouclier) et « Redémarrage requis » hors du libellé du bouton.

### Pilotes
- **Rôle** : comparer le pilote NVIDIA, le chipset AMD et les pilotes Windows Update, et installer après confirmation (seule exception à la réversibilité).
- **Actions** : Rechercher à nouveau ; Télécharger et installer la version X ; Notes de version ; Annuler le téléchargement ; cocher des pilotes Windows Update et Installer la sélection.
- **Problèmes clés** :
  - Boutons sans nom.
  - États et avertissements en TextMuted ; consigne de prudence placée **après** le bouton d'installation.
  - Versions répétées 3 à 4 fois par carte.
  - Cartes GPU et chipset dupliquées.
  - Pas de bilan après l'installeur (installation annulée invisible).
  - Progression Windows Update placée ailleurs, sans barre ; cases modifiables pendant l'installation ; catégories en anglais.
  - Erreurs brutes sans action.
  - Mention NVIDIA sur un PC AMD ; impasse pour AMD et Intel (aucun lien).
  - Cartes qui disparaissent pendant la recherche (décalage de mise en page).
  - Verdict divergent avec le contrôle « âge du pilote » du Diagnostic.
- **Pistes** :
  - Un modèle unique « pilote installable » : badge d'état, tableau Installé / Disponible (version, date, taille), message réservé aux cas particuliers.
  - Même composant de progression pour les 3 sources ; bilan persistant après l'installation.
  - Consigne en InfoBar au-dessus de la liste ; bouton « Installer (2)… » masqué si la liste est vide.
  - Résultats précédents gardés (atténués) pendant une nouvelle recherche.
  - Textes selon le fabricant détecté ; lien vers la page officielle AMD ou Intel.
  - Libellés de verdict alignés avec le Diagnostic.

### Mesures
- **Rôle** : capture PresentMon manuelle, liste des captures (manuelles et automatiques), détail, comparaison avant / après, graphe des temps d'image.
- **Actions** : choisir le jeu (liste ou nom d'exe), la durée, le délai et le libellé ; Démarrer, Annuler ; Supprimer ; sélectionner 1 ou 2 captures (Ctrl+clic) ; Choisir ou changer PresentMon.
- **Problèmes clés** :
  - Mesures automatiques absentes jusqu'au redémarrage.
  - Colonnes FPS et 1 % low coupées (565 px).
  - Pas de défilement : graphe rogné à la hauteur minimale.
  - Mieux / moins bien par la couleur seule ; tableau de comparaison sans en-têtes.
  - Légende loin du graphe ; série « Après » en orange d'avertissement ; graphe invisible pour l'UIA.
  - Deux champs cibles concurrents, dont la priorité est cachée.
  - Rien n'indique que le jeu doit déjà tourner (erreur après 5 min).
  - 3e sélection ignorée en silence ; avant / après imposé par la date.
  - « Supprimer » (Ghost) actif sans sélection.
  - Statuts d'échec en vert.
  - Avertissement « 1 % low peu fiable » noyé.
  - Chemin de PresentMon sur la page.
  - Lecture du CSV sur le thread UI.
- **Pistes** :
  - Page défilante, graphe proportionnel et agrandissable.
  - Liste compacte (Libellé, FPS, 1 % low d'abord, chiffres alignés à droite), état vide de premier usage.
  - Comparaison par deux listes « Avant » / « Après » avec « Inverser » et avertissement si le jeu ou le réglage diffère.
  - Écart = flèche, mot et couleur.
  - Jetons `Series1/2`, légende dans la carte du graphe, résumé UIA.
  - Cible = ComboBox éditable, présélection de la partie en cours, rappel « Lancez le jeu, puis démarrez ».
  - InfoBar de statut et barre de progression du compte à rebours.
  - Badge « Peu fiable ».
  - PresentMon déplacé dans Paramètres › Mesures.
  - Suppr, menu contextuel (Renommer, Supprimer…, Ouvrir le CSV).

### Paramètres
- **Rôle** : démarrage, mises à jour, dock, pendant les parties, mesure automatique, IGDB, dossiers de jeux, données.
- **Actions** : cases appliquées immédiatement ; Rechercher maintenant, Installer maintenant, Voir les nouveautés ; curseurs du dock ; Enregistrer et tester (IGDB) ; Supprimer les identifiants ; Ajouter ou retirer un dossier ; Ouvrir le dossier.
- **Problèmes clés** :
  - Ordre des sections peu logique ; page longue sans navigation interne.
  - Paragraphes d'aide jusqu'à 429 caractères, en TextMuted.
  - Sous-réglages du dock actifs quand le dock est désactivé (seul le délai est grisé).
  - PasswordBox non vidé après enregistrement (l'état affiché contredit l'état réel).
  - « Supprimer les identifiants » sans confirmation, en Ghost.
  - Statut IGDB en ✓/✗ neutre, avec message brut.
  - Mesure automatique cochable sans PresentMon (ignorée en silence).
  - `schtasks` sur le thread UI ; case de démarrage fausse le temps de la lecture.
  - Liste du mode de mise à jour sans libellé visible.
  - « Installer maintenant » grisé ici mais masqué dans le bandeau.
  - Aucune date de dernière recherche ni raison du report ; pas d'« Ouvrir le journal ».
- **Pistes** :
  - Sections Général (Démarrage, Pendant les parties), Mes jeux (Dossiers, Jaquettes), Dock, Mesures (mesure automatique et PresentMon avec son état), Mises à jour et à propos, Données (Ouvrir le dossier, **Ouvrir le journal**).
  - Gabarit « ligne de réglage » façon Paramètres Windows 11 (titre, une phrase, contrôle à droite, « En savoir plus » repliable).
  - Dépendances par `IsEnabled` du parent.
  - Statuts typés (succès ou erreur) en InfoBar, annoncés.
  - Tutoriel IGDB replié une fois configuré, bouton « Copier » pour `http://localhost`.

### Dialogues
- **Rôle** : confirmer (une correction, un lot, une installation de pilote), choisir (jeux installés, programmes à fermer, jaquette IGDB), informer ou signaler une erreur (MessageBox).
- **Actions** : Appliquer, Appliquer les N, Installer (case « je comprends »), Créer les profils cochés, Ajouter la sélection, Rechercher, Utiliser cette jaquette, Annuler ; Oui / Non / OK.
- **Problèmes clés** :
  - 45 MessageBox non thémées, en « Oui / Non ».
  - « Annuler « X » ? ».
  - Bouton principal générique (« Appliquer ») au lieu du verbe de l'action.
  - Bouton par défaut non signalé ; focus initial absent ; ordre de tabulation inversé.
  - Listes à cocher probablement inaccessibles au clavier ; éléments nommés d'après le type .NET.
  - ConfirmChange et DriverInstall sans défilement (débordement à 125-150 %) ; MaxHeight 760 fixe ailleurs.
  - « Ce qui change » en chemin de registre.
  - Badges d'exigences sous 3 formes.
  - GameScan : texte périmé (Steam seul), vocabulaire « profil », pas de « Tout cocher », s'ouvre même sans résultat, bouton actif sans sélection.
  - IGDB : Entrée relance la recherche et perd la sélection ; erreur brute en gris ; « … » éternel quand un aperçu échoue.
  - Sélecteurs incohérents (ligne cochable dans l'un, pas dans l'autre).
- **Pistes** :
  - Un gabarit `DialogWindow` (en-tête avec instruction principale, corps défilant, pied fixe, `MaxHeight` = zone de travail moins une marge) et une API `Confirm(title, message, confirmLabel, isDestructive)`, `Info`, `Error` (avec « Afficher les détails » et « Copier »).
  - Boutons nommés par le verbe (« Retirer le jeu », « Supprimer », « Quitter et restaurer », « Restaurer », « Installer sans point de restauration »), variante Danger.
  - Bouton par défaut signalé ; focus sur « Annuler » dans les confirmations à risque.
  - « Ce qui change » en langage courant, avec « Détails techniques » repliés.
  - Composant « liste à cocher » partagé (ligne entière = case, « Tout sélectionner », compteur dans le bouton, filtre).
  - État vide sans ouvrir le dialogue.
  - Garder la MessageBox **uniquement** pour App.xaml.cs:310 (avant DI et thème).

### Dock flottant
- **Rôle** : lancer en un clic les jeux épinglés depuis le bureau ; raccourci vers OptiGame.
- **Actions** : clic = Jouer ; clic droit = Jouer, Ouvrir la page du jeu, Retirer du dock ; glisser = réordonner ; logo = ouvrir OptiGame.
- **Problèmes clés** :
  - Bande de déclenchement de 2 px au-dessus de la barre des tâches ; masquage qui ne se déclenche pas toujours ; repli à chaque écriture de settings.json.
  - Activation sans aperçu.
  - Réordonnancement uniquement par glisser ; cellules sans rôle UIA.
  - Aucun retour au lancement ; jeu désinstallé non signalé ; pastille « en cours » jamais visible (dock fermé pendant les parties).
  - Pas de menu sur le plateau ou le logo (position, masquage, réglages, désactiver).
  - État vide non cliquable et peu contrasté ; opacité réglable jusqu'à 0 %.
  - Débordement silencieux au-delà de N jeux (bord vertical à 128 px : 4 jeux en 1080p).
  - Géométrie non mise à jour sur WM_SETTINGCHANGE, WM_DISPLAYCHANGE ou WM_DPICHANGED.
  - Rayon et initiales fixes ; jaquettes décodées à 320 px (floues au survol en haute densité).
  - Animations sans respect de « Effets d'animation ».
  - Menu probablement fermé par le minuteur (à vérifier).
  - Le dock peut devenir `Application.MainWindow` au démarrage réduit, donc propriétaire des boîtes de dialogue (plausible).
- **Pistes** :
  - Détecter le bord de la barre des tâches (côté Platform).
  - Minuteur lancé dans `OnTriggerEnter` ; `ApplySettings` seulement si une propriété Dock* change, jamais pendant un survol ou un glisser.
  - Aperçu d'environ 2 s quand on change un réglage.
  - Menu « Déplacer vers… » et ordre éditable dans la fenêtre principale ; `AutomationPeer` avec Invoke.
  - État « Lancement… » sur la jaquette (12 s, clics ignorés) ; jaquettes désinstallées grisées.
  - Menu du plateau (Ouvrir OptiGame en gras, Position ▸, Masquer automatiquement, Paramètres du dock…, Désactiver).
  - État vide cliquable (« Épingler des jeux… ») ; opacité minimale d'environ 30 %.
  - Réduction automatique de la taille des icônes.
  - Jetons `Dock.*`, rayon proportionnel, décodage via `ImageLoader.PixelsFor`.
  - Supprimer la pastille « en cours ».
  - Ne jamais laisser le dock devenir `MainWindow`.

---

## 5. À préserver absolument

**Principes produit (CLAUDE.md)**
1. **Rien n'est appliqué sans action explicite.** Chaque confirmation montre ce qui change, pourquoi, et si des droits admin ou un redémarrage sont requis. « Annuler » / « Non » reste le choix par défaut, y compris pour Entrée et Échap. Les options avancées demandent une double confirmation (case obligatoire). Aucun badge, raccourci ou bouton unique de la coque ne doit contourner la confirmation.
2. **Réversibilité** : la vue Simple n'applique que les corrections non avancées des contrôles « À corriger », jamais les facultatives. « Désactiver » n'annule que les ids `fix.`, jamais `game.`. Chaque correction reste restaurable séparément ; un échec de restauration la laisse listée. L'état est toujours **relu** (Diagnostic, NVIDIA, Graphismes), jamais stocké.
3. **Pilotes, seule exception** : `DriverInstallDialog` avec case « je comprends » obligatoire, texte « non annulable », chemin du retour en arrière (Gestionnaire de périphériques → Restaurer le pilote), point de restauration proposé et coché s'il est disponible, sinon raison affichée ; si sa création échoue, l'utilisateur décide (« Non » par défaut). Téléchargement limité aux hôtes officiels, signature vérifiée, installeur refusé jamais ouvert. Windows Update : rien n'est coché d'office.
4. **Les erreurs importantes restent visibles dans la fenêtre ET écrites au journal**, en plus des notifications (masquables sous AtlasOS). Une notification n'est jamais le seul canal ; chacune est journalisée.
5. **Auto HDR n'est jamais écrit** : il est seulement affiché, avec un lien vers `ms-settings:display-advancedgraphics`. GpuPreference n'est proposé que sur les PC à plusieurs cartes. La carte NVIDIA est masquée sans pilote NVIDIA.
6. **Jeux** : jamais retirés automatiquement ; jamais le jeu en cours ; « Disque absent » n'est jamais traité comme désinstallé ; le retrait ne supprime que le profil et la confirmation cite les réglages `game.*` conservés. Lancements, installations et liens passent toujours sans droits admin (`UnelevatedLauncher`, `explorer.exe`).
7. **Données et textes obligatoires** : seul le nom du jeu part vers IGDB et PCGamingWiki, seul le modèle de carte vers NVIDIA. Les mentions de confidentialité, la source « Voir sur PCGamingWiki » et la licence CC BY-NC-SA, le caractère « estimé » d'une note et « durée inconnue » après un plantage sont à conserver (reformulables). Les avertissements sur l'arrêt forcé des programmes et sur la priorité Haute ou les anti-cheats sont à déplacer en ligne, pas à supprimer.
8. **Interface en français**, identifiants en anglais. Le nom reste « OptiGame » ; logo vectoriel (`Logo.Image`/`Logo.Mark`) peint avec les brosses du thème.

**Thème**
9. Aucune couleur codée en dur dans les vues : toute nouvelle couleur devient un jeton `Brush.*` de Theme.xaml. `Brush.Ok/Warning/Info/Danger` et leurs variantes `Soft` sont lus **par nom** par `StatusToBrushConverter` ; les renommer oblige à modifier le convertisseur.
10. L'accent Fluent reste dans les ressources **directes** de `App.xaml`, y compris les clés `SystemColors.AccentColor…Key` (les clés `Accent*Brush` seules ne suffisent pas). Référencer `Color.Accent` est permis (à vérifier), déplacer ces clés ne l'est pas. Garder `ThemeMode="Dark"` et `ShutdownMode="OnExplicitShutdown"`.

**Architecture WPF / MVVM**
11. Une page = un ViewModel **singleton** ; la vue est choisie par **DataTemplate implicite** (MainWindow, fiche du jeu dans LibraryView, chipset dans DriversView), **jamais** par `ContentTemplate` explicite.
12. **MainWindow est transitoire** : fermée (pas masquée) pendant les parties, puis recréée à la même page et à la même place (réduite si elle l'était). Tout état de la coque (barre repliée, page, saisie en cours) vit dans un ViewModel ou dans settings.json, jamais dans la vue.
13. Les vues s'abonnent aux événements des ViewModels sur `Loaded`/`Unloaded`, **jamais** sur `DataContextChanged`. `DockWindow.OnClosed` se désabonne de `CompositionTarget.Rendering`.
14. **Les dialogues restent modaux et possédés** (`ShowOwned`, `ActiveWindow()` qui exclut une fenêtre fermée ou masquée et le dock). `CloseMainWindowForGame` s'appuie sur `IsThreadModal` et `OwnedWindows.Count` : une superposition dans la fenêtre casserait cette détection, sauf à adapter la condition.
15. Le message « fichier d'état illisible » (App.xaml.cs:310) est bloquant, s'affiche avant DI et thème, et ne propose jamais d'écraser ou de réinitialiser le fichier.
16. Ordre de démarrage : `InGameFootprint.Start` avant `StartSessions` ; `PlaytimeTracker` avant `Recover` ; `--minimized` = pas de fenêtre ; `quit.request` → `EndNow` puis `Shutdown` ; « Quitter » pendant une partie → confirmation, puis `EndNow`, puis `Shutdown`. `DispatcherUnhandledException` est journalisée avec `Handled = true`.
17. Navigation : `NavItem.SetSelectedSilently` et `NavigationService.NavigateRequested`. La première analyse du Diagnostic ne se lance que si `!HasResults` ; la recherche des pilotes une seule fois par exécution.
18. Garde des modifications non enregistrées (`CloseGamePage`, `PlayAsync`) à **étendre** à toutes les sorties (dock, Quitter, raccourcis), jamais retirée. Barre d'enregistrement hors du ScrollViewer. `GraphicsPreset`, jaquettes et `DockOrder` ne sont jamais écrits par `ProfileStore.Save` (respectivement `SetGraphicsPreset`, `SetArtwork`, `SetPinned`/`MoveInDock`).
19. Le nom accessible d'`Expander.Row` est transmis au ToggleButton par `TemplateBinding` ; `Text.Status` se replie quand il est vide.

**Performance et sobriété (principe 4)**
20. Jaquettes : `CoverImage` avec `IsAsync=True`, décodées une seule fois par `ImageLoader` (cache LRU de 96 Mo, images figées, largeur réelle = `GridCoverWidth` 198 × `DisplayScale`, gris = pixels Gray8 **copiés**). Toute nouvelle largeur de carte doit mettre à jour `GridCoverWidth`. Jamais de `PathToImageConverter` synchrone dans les grilles. Fiche, dock et IGDB passent aussi par `ImageLoader` (ajuster `ConverterParameter`).
21. Non installés affichés par pages de 48 (aucun `WrapPanel` virtualisé en WPF). La page suivante ne se charge qu'en descendant ou quand le contenu grandit, jamais fenêtre masquée. « Afficher plus » reste pour le clavier. Filtre changé → 1re page et retour en haut ; relecture → même nombre de cartes ; recherche après 200 ms sans frappe.
22. `MemoryRelief` (fenêtre masquée ou début de partie) et `GameTimeGate` (lectures lourdes reportées à la fin de la partie). Pendant une partie, fenêtre et dock sont **fermés** si `LightDuringGames` : aucune animation, minuterie ou interrogation périodique ne tourne fenêtre fermée.
23. Lectures lentes hors du thread UI, avec numéro de version (la plus récente l'emporte). Curseurs avec `Delay=250`.

**Dock**
24. `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, `ShowActivated=False` ; titre « OptiGame — Dock » (captures de test) ; zones d'alpha 0 traversées par les clics, bande `#01000000`, plateau jamais à alpha 0.
25. Grossissement par transformations de **rendu** (jamais `LayoutTransform`) ; un `TransformGroup` non gelé par cellule ; boucle `Rendering` arrêtée au repos ; glisser par capture de la souris et `DockReorder` (jamais DragDrop OLE) ; ordre modifié seulement par `SetPinned`/`MoveInDock` (`Move` déplace d'abord l'élément affiché).
26. Pile des fenêtres en mode « collé au bureau » (`LowestWindowAboveDesktop`, jamais `HWND_BOTTOM`), Win+D ; `UpdateSuppression` ne crée jamais la fenêtre ; le plein écran est réévalué avant chaque apparition. Toute alternative clavier au dock vit dans la fenêtre principale.

**Mesures et zone de notification**
27. Un CSV non reconnu est **gardé** (`-non-reconnu.csv`) ; on ne supprime jamais un fichier qu'on n'a pas lu. Session ETW de la capture manuelle distincte de celle de la mesure automatique. Sélection multiple synchronisée par le code-behind (une seule source de vérité Avant/Après). `FrametimeChart` : min/max par pixel, brosses figées, écrêtage à 99,9 % × 1,5, repères 240/144/60/30 FPS.
28. Icône de notification : `ForceCreate(enablesEfficiencyMode:false)`, chargée à la taille exacte de `IconMetrics` depuis OptiGame.ico. Toute variante d'état suit le même chemin.
29. Mises à jour : installation automatique seulement si `WhyNotNow` est nul, 30 s **après** la fermeture de la fenêtre (`MainWindowHidden`). Seule la copie installée se met à jour seule.

---

## 6. Points forts à conserver

- Un thème centralisé, largement adopté : palette `Color.*`/`Brush.*`, `Text.*` (285 emplois), Card, boutons. Les couleurs principales passent toutes l'AA (Text 11,3 à 16,5 ; TextSecondary 5,5 à 8,0 ; Accent, Warning et Info ≥ 5,1 ; OnAccent sur Accent 9,5).
- Des confirmations exemplaires pour la confiance : quoi, pourquoi, exigences, « Annuler » par défaut, double confirmation pour les options avancées et les pilotes ; le dialogue d'installation de pilote est un modèle (non-réversibilité en gras, retour en arrière, point de restauration).
- Une microcopie honnête et rassurante qui dit aussi ce qui n'est **pas** fait (« Rien n'est retiré automatiquement… », « Rien n'est installé sans votre confirmation »), une note marquée « estimée » (≈) et une source citée.
- La divulgation progressive : vue Simple à bouton unique et liste exacte de ce qui sera appliqué ; vue Avancé triée par statut ; lignes repliables avec résumé d'une ligne.
- Une navigation proche du NavigationView : RadioButton (une seule page active), indicateur de forme de 3 px, noms accessibles posés.
- La zone de notification conforme aux usages : clic gauche immédiat, action par défaut en gras, instance unique, confirmation de « Quitter » seulement pendant une partie.
- Des bandeaux dans la fenêtre en plus des notifications (détection, mises à jour) : la bonne réponse au masquage sous AtlasOS, à généraliser.
- Des noms accessibles contextualisés déjà présents à généraliser : « Jouer à {0} », « Retirer {0} de Mes jeux », « Installer {0} », « Ajouter {0} à Mes jeux », curseurs, filtres, cases Windows Update, lignes repliables.
- Le menu contextuel des jaquettes, qui double les actions de survol ; un liseré de focus sur les jaquettes ; une épingle en `Hidden` (pas de saut de mise en page).
- La barre « Modifications non enregistrées » collante (point + texte, pas la couleur seule), avec garde au retour et avant « Jouer » ; l'aperçu exact de la commande de lancement ; « Ce que fera ce profil » généré par le moteur.
- Les fonctions sans objet masquées (NVIDIA sans pilote NVIDIA, carte graphique sur PC à une carte, Page Steam sans appid) ; des états de chargement prévus pour les lectures asynchrones.
- Une comparaison honnête (écart < 1 % neutre, sens de P99 respecté), un graphe fidèle aux saccades, un compte à rebours de capture.
- Le dock : retour tactile soigné (enfoncement, onde, rebond, annulation hors cible), glisser lisible, effacement total pendant les jeux et le plein écran, mode « collé au bureau » bien pensé.
- Une ingénierie mémoire et performance mesurée et documentée : aucune saccade avec des centaines de jaquettes.
- Une typographie française partiellement juste (guillemets « », vrais « … ») ; une interface entièrement en français.

---

## 7. Glossaire proposé (un terme par concept)

| Concept | Terme retenu (interface) | À bannir côté utilisateur | Où ça dérape aujourd'hui |
|---|---|---|---|
| Période où un jeu tourne et où OptiGame a appliqué ses réglages | **partie** (« Partie en cours », « pendant la partie ») | session | MainWindow.xaml:46, App.xaml:64, GamePageView.xaml:87, SessionViewModel.cs:89 |
| Fin anticipée de cette période | **Arrêter l'optimisation et restaurer** | Terminer et restaurer / Terminer la session et restaurer les réglages | MainWindow.xaml:50, App.xaml:64 |
| Jeu suivi par OptiGame | **jeu** (« Ajouter à Mes jeux », « Retirer de Mes jeux… ») | profil (créer, profil existant) | GameScanDialog.xaml:9-12, LibraryView.xaml:139 |
| Réglages appliqués au lancement et restaurés à la fermeture | **réglages de partie** ; interrupteur « Optimiser ce jeu pendant les parties » | profil, « Appliquer automatiquement », « Profil désactivé » | GamePageView.xaml:299, 349, 579 |
| Profil du pilote NVIDIA | **profil NVIDIA** | profil global, profil du pilote | FrameCapViewModel.cs:69-70 |
| Vérification du Diagnostic | **contrôle** | point | DiagnosticViewModel.cs:232, 313 |
| Changement journalisé proposé ou appliqué par OptiGame | **optimisation** (« Optimisations actives ») | correction, modification ; « réglage » dans ce sens | DiagnosticView.xaml:175, 217 ; ConfirmChangeDialog.xaml:4 |
| Valeur de Windows, du pilote ou du profil | **réglage** ; valeur sauvegardée = **réglage d'origine** | état d'origine, valeur d'origine (en vrac) | partout |
| Écrire une optimisation (avec confirmation) | **Appliquer…** (« Appliquer les 3 optimisations… ») | Activer, Confirmer (comme verbe d'action) | DiagnosticViewModel.cs:318, ConfirmChangesDialog.xaml.cs:17 |
| Défaire une optimisation ou un réglage permanent | **Restaurer l'original…** / **Tout restaurer…** | Annuler, Désactiver, Rétablir | DialogService.cs:70, DiagnosticView.xaml:227, GamePageView.xaml:433 |
| Fermer sans rien faire | **Annuler** (uniquement) | — | — |
| Jeter une saisie du formulaire | **Abandonner les modifications** | Annuler les modifications | GamePageView.xaml:22 |
| Valider un formulaire (profil) | **Enregistrer** | — | — |
| Contrôle qui n'a pas pu s'exécuter | **Non vérifié** | Erreur (rangée dans « À savoir ») | DiagnosticViewModel.cs:217-222 |
| Niveaux de résultat d'un contrôle | **OK · À corriger · À savoir · Non vérifié** | Info | DiagnosticViewModel.cs:356 |
| Enregistrement PresentMon | **mesure** (« Démarrer la mesure », « Mesures enregistrées », « Mesure auto ») | capture | MeasuresView.xaml:69, 109 |
| Steam, Epic Games, GOG | **magasin** (« Tous les magasins ») | plateforme | LibraryView.xaml:287, LibraryViewModel.cs:262 |
| Jeux possédés sur un magasin | **bibliothèque** | — | — |
| Programme qui démarre le jeu | **lanceur** (uniquement) | magasin, dans ce sens | LibraryViewModel.cs:585, 676 |
| Écran OptiGame d'un jeu | **fiche du jeu** (« Ouvrir la fiche ») | page du jeu | LibraryView.xaml:384, DockWindow.xaml.cs:692 |
| Page du magasin | **Voir sur Steam** | Page Steam / page du jeu | GamePageView.xaml:115-118 |
| Sections de la barre latérale | **page** (« page Diagnostic ») | onglet | ConfirmChangeDialog.xaml:30, GameScanDialog.xaml.cs:16 |
| Modes du Diagnostic | **vue simple / vue avancée** | vue Avancé, onglet Avancé | DiagnosticView.xaml:100, 203 |
| Filtrer la grille par nom | **Rechercher dans Mes jeux** | Filtrer les jeux… | LibraryView.xaml:220-223 |
| Analyser le disque pour ajouter des jeux | **Détecter les jeux installés…** | Rechercher des jeux installés | LibraryView.xaml:256 |
| Relire, réanalyser, revérifier | **Actualiser** (F5) | Relancer l'analyse, Rechercher à nouveau, Rechercher maintenant | DiagnosticView.xaml:94, DriversView.xaml:20, SettingsView.xaml:49 |
| Choisir un fichier | **Parcourir…** | Changer…, Choisir PresentMon… | MeasuresView.xaml:34, 78 |
| Effacer des données (mesures, identifiants) | **Supprimer…** | Retirer, dans ce sens | SettingsView.xaml:160 |
| Notes de version | **Voir les nouveautés** | Nouveautés | MainWindow.xaml:92, 107 |
| Exigences d'une optimisation | **Droits administrateur** (icône bouclier) · **Redémarrage requis** | admin, redémarrage | DiagnosticViewModel.cs:389, ConfirmChangesDialog.xaml.cs:21 |
| États d'un jeu | **Désinstallé** · **Disque absent** · **Partie en cours** · **Optimisation désactivée** | En cours / Session en cours / Profil désactivé | LibraryView.xaml:135-147 |
| Dock | **Épingler au dock / Retirer du dock** (déjà cohérent) | — | — |
| Métriques | **FPS moyens** · **1 % les plus lents (1 % low)** · **Temps d'image P99 (ms)** | Frametime, Frametimes | MeasuresView.xaml:175, 242 |
| Identifiant Steam | **Numéro du jeu sur Steam (appid)** | Appid Steam | GamePageView.xaml:384 |

**Règles typographiques associées** : points de suspension sur toute commande qui demande une saisie ou une confirmation ; casse de phrase, les capitales venant d'un style `Text.Overline` et jamais de la chaîne ; accord réel des pluriels (fonction commune `Plural`) ; espace fine insécable (U+202F) avant `: ; ? ! %` et à l'intérieur des « » ; un seul formateur de dates (« 22 sept. 2026 », « aujourd'hui à 14:32 », relatif pour les parties).
