# Système de design d'OptiGame

Référence pour toute modification de l'interface (WPF, `src/OptiGame.App`). Source unique : `Themes/Theme.xaml`.
Rendu de référence : `.\scripts\ui-snapshots.ps1` → `artifacts\ui-snapshots\<Label>\0-galerie.png` (toutes les pages aussi).

## Principes

1. **Une seule action principale par écran** (`Button.Primary`, vert). Le reste : `Secondary`, `Ghost`, ou menu « … ».
2. **Un statut n'est jamais porté par la couleur seule** : icône de forme + mot (OK, À corriger, À savoir, Non vérifié).
3. **Un rôle par couleur.** Le vert de la marque = marque, action principale, sélection. Jamais un statut, une valeur
   (FPS, note) ni une décoration.
4. **Deux modèles de validation** : formulaire → « Enregistrer » ; réglage de Windows ou du pilote → « Appliquer… »
   (confirmation : quoi, pourquoi, droits administrateur, redémarrage ; « Annuler » par défaut) puis « Restaurer l'original… ».
5. **Ce qui compte s'affiche dans la fenêtre** (InfoBar), en plus de la notification Windows (masquable) et du journal.
6. **Clavier d'abord** : focus visible (`Focus.Ring`), tout faisable au clavier, nom accessible sur chaque contrôle,
   statuts annoncés (`LiveSetting`), titres structurés (`HeadingLevel`), animations seulement si Windows les autorise.
7. **Aucune valeur en dur dans les vues** : couleur, taille de police, rayon, couleur d'un graphe → une clé du thème.

## Couleurs

Contrastes WCAG 2.x calculés sur les surfaces réelles (fonds « Soft » mélangés à leur opacité).

| Rôle | Clé | Valeur | Contraste |
|---|---|---|---|
| Fond de fenêtre / barre latérale | `Brush.Window` / `Brush.Sidebar` | #0E1014 / #12151B | — |
| Carte, survol | `Brush.Card` / `Brush.CardHover` | #181C23 / #1F242D | — |
| Contrôle, survol, appui, sélection | `Brush.Control` / `ControlHover` / `ControlPressed` / `ControlSelected` | #232833 / #2B313D / #1C2028 / #343B48 | — |
| Contour de carte / de contrôle | `Brush.Border` / `Brush.ControlBorder` | #262C36 / #3A4250 | — |
| Texte | `Brush.Text` | #EDEFF3 | ≥ 9,8:1 partout |
| Texte secondaire | `Brush.TextSecondary` | #A2A9B4 | ≥ 4,7:1 partout |
| Légendes, aides | `Brush.TextMuted` | #9099A6 | ≥ 4,5:1 (pas sur `ControlSelected` : 3,9) |
| Contrôle désactivé | `Brush.TextDisabled` | #6B7280 | exempté (WCAG 1.4.3) |
| Marque · action · sélection | `Brush.Accent` (+ `Hover`, `Pressed`, `Soft`), texte dessus `Brush.OnAccent` | #2FD27A | OnAccent 9,5:1 |
| Succès | `Brush.Ok` / `Brush.OkSoft` | #6CCB5F | ≥ 6,4:1 sur son fond doux |
| Avertissement | `Brush.Warning` / `Brush.WarningSoft` | #FCE100 | ≥ 9:1 |
| Erreur, action destructrice | `Brush.Danger` / `Brush.DangerSoft`, texte dessus `Brush.OnDanger` | #FF99A4 | ≥ 6,4:1 ; OnDanger 9:1 |
| Information | `Brush.Info` / `Brush.InfoSoft` | #60CDFF | ≥ 7:1 |
| Séries de graphe (avant / après) | `Brush.Series1` / `Brush.Series2` | #60CDFF / #FF9F43 | bleu / orange : lisibles par les daltoniens |
| Focus clavier | `Brush.FocusRing` | #FFFFFF | ≥ 13:1 sur les surfaces |
| Voiles sur image | `Brush.Scrim` / `ScrimStrong` / `ScrimLight` / `Overlay` / `OverlayBorder` | noir 80 % / 90 % / 60 %… | — |
| Remplaçants d'image | `Brush.CoverPlaceholder` / `CoverPlaceholderMuted` / `BannerPlaceholder` / `BannerFade` / `DockPlaceholder` | dégradés | — |
| Dock | `Color.DockPlate`, `Brush.DockPlate`, `Brush.DockBorder`, `Color.Highlight` | — | — |
| Bande du dock | `Brush.HitTestOnly` | #01000000 | alpha 1 VOLONTAIRE : reçoit la souris |

Les couleurs de statut sont celles de Windows 11 (Fluent sombre). `StatusToBrushConverter` lit `Brush.Ok/Warning/Info/Danger`
(+ `Soft`) **par nom**. L'accent des contrôles Fluent est redéfini dans `App.xaml` (ressources directes) : les 8 valeurs
`Color` y recopient `Color.Accent*` (alias refusé par le compilateur XAML, MC2000), les brosses référencent les jetons.

## Typographie

Échelle 12 · 14 · 16 · 20 · 28 · 40 (`FontSize.*`), police Segoe UI Variable, icônes Segoe Fluent Icons (`IconSize.*` 12/16/20/28).

| Style | Taille | Usage |
|---|---|---|
| `Text.Hero` | 40 SemiBold | nom du jeu (bannière) |
| `Text.PageTitle` | 28 SemiBold, titre niveau 1 | titre de page |
| `Text.Stat` | 28 SemiBold, couleur du texte | chiffre clé (FPS, note) |
| `Text.Title` | 20 SemiBold, niveau 2 | titre de dialogue, verdict |
| `Text.SectionTitle` | 16 SemiBold, niveau 2 | titre de section ou de carte |
| `Text.BodyStrong` / `Text.Body` / `Text.Secondary` | 14 | texte courant |
| `Text.Caption` | 12, TextMuted | légende, aide d'une ligne |
| `Text.Mono` | 12 Cascadia Mono | détails techniques (repliés) |
| `Text.Status` | 14 SemiBold, neutre, région active | ligne de statut (succès / échec → InfoBar) |
| `Text.FieldLabel` (Label) | 14 | libellé relié à son champ (`Target`) |
| `Text.Label` | 13 (marge intégrée) | hérité, à remplacer par `Text.FieldLabel` |

Casse de phrase partout ; jamais de capitales tapées dans une chaîne.

## Espacements et rayons

Grille de 4 : `Space.XS` 4 · `S` 8 · `M` 12 · `L` 16 · `XL` 24 · `XXL` 32. Marges prêtes : `Padding.Page` (32,24,32,32),
`Padding.Card` (20), `Padding.CardCompact` (16,12), `Padding.Dialog` (24), `Margin.StackXS…XL` (sous un élément),
`Margin.InlineS/M` (à droite). Rayons (Fluent) : `Radius.Control` 4, `Radius.Card` 8, `Radius.Cover` 8, `Radius.Pill` 12,
`Radius.Round` (cercle).

## Focus

`Focus.Ring` (anneau blanc 2 px à 3 px du contrôle), `Focus.RingCard` (cartes, jaquettes), `Focus.RingInset` (élément collé à
un bord). Tous les styles maison le posent en `FocusVisualStyle` ; WPF ne l'affiche qu'au clavier. Ne jamais mettre
`FocusVisualStyle="{x:Null}"` sans état de focus équivalent.

## Composants

| Composant | Clé / type | Règles |
|---|---|---|
| Boutons | `Button.Primary`, `PrimaryLarge` (Jouer), `Secondary`, `SecondarySmall` (28 px), `Ghost`, `Danger`, `DangerFilled`, `Icon` | 32 px de haut (cible ≥ 24) ; verbe de l'action ; « … » si une confirmation ou une saisie suit ; pas de curseur main (usage Windows) |
| Bouton icône | `controls:IconButton` (`Glyph`, `Label`, `IsLabelVisible`) | le libellé est TOUJOURS le nom accessible (et l'infobulle en icône seule) ; s'utilise avec tout style `Button.*` |
| InfoBar | `controls:InfoBar` (`Severity` Info/Success/Warning/Error, `Title`, `Message`, contenu = actions, `IsClosable`, `CloseCommand`, `CloseLabel`) | fond + icône de forme par gravité ; nom UIA « Gravité : titre message » ; annoncée à l'apparition (assertive pour Error) |
| Badges | `Badge.Neutral/Success/Warning/Danger/Info/Overlay` | toujours un mot (icône facultative) ; 12 SemiBold |
| Carte, tuile | `Card`, `Tile` | rayon 8, contour `Brush.Border` |
| Ligne repliable | `Expander.Row` | nom accessible transmis au bouton d'en-tête ; résumé d'une ligne dans l'en-tête |
| Segments | `Segment` (RadioButton) | sélection = fond + texte + trait d'accent (pas la teinte seule) |
| Navigation | `NavButton` | trait d'accent = sélection ; focus visible |
| Logo | `Logo.Image` (tuile), `Logo.Mark` (symbole sur `Brush.Accent`) | vectoriel, géométries `Logo.Dial` / `Logo.Play` partagées |

| Exigences | `controls:RequirementBadges` (`RequiresAdmin`, `RequiresReboot`) | « Droits administrateur » (bouclier), « Redémarrage requis » : seuls libellés autorisés |
| Divulgation | `Expander.Disclosure` | chevron + texte secondaire (« Détails techniques ») ; `AutomationProperties.Name` plus précis si plusieurs dans une liste |
| Liste à cocher | `CheckList` + `CheckList.Item` (ListBox `SelectionMode=Multiple`) | ligne entière = case (clic, Espace), flèches ; éléments : `CanSelect`, `AccessibleName` ; la sélection = `SelectedItems` ; bouton « Ajouter 3 jeux » inactif à 0 |

| Jaquette | `controls:CoverTile` (`Image` lié avec IsAsync, `Initials`, `Title`, `Caption`, `CaptionGlyph`, `IsDimmed`, `TopLeft`, `TopRight`, `Actions`) | une seule pour installés et non installés ; bouton focalisable : Entrée = action principale, touche Menu = toutes les actions (menu contextuel complet) ; `Actions` = raccourcis souris hors tabulation (`Button.CoverPlay`, `Button.CoverRound`) ; voile sur l'image seulement ; zoom de survol seulement si les effets d'animation de Windows sont activés ; nom accessible = jeu + tout ce que disent les pastilles |
| Bouton à menu | Button + ContextMenu ouvert sous le bouton (« Ajouter des jeux ▾ ») | nom accessible + HelpText « Ouvre un menu » ; choix avec « … » |

Grilles de jaquettes : `ItemsControl` + `WrapPanel`, `KeyboardNavigation.TabNavigation="Once"` (Tab entre et sort de la
grille, flèches d'une jaquette à l'autre).

| Ligne de réglage | `controls:SettingRow` (`Header`, `Description`, `Glyph`, contenu = contrôle, `Details` repliés) | façon Paramètres de Windows 11 ; le contrôle porte le nom accessible de la ligne |
| Ligne de liste | `ListItem.Selectable` (ItemContainerStyle) | sélection = fond + trait d'accent, jamais un aplat vert |

## Coque (fenêtre principale)

- **Navigation** façon NavigationView : pages de travail en haut (Mes jeux, Diagnostic, Pilotes, Mesures), Paramètres en
  pied. Sous 1008 unités de large (`MainWindow.CompactNavigationWidth`), barre de 64 : icônes seules, info-bulle = titre et
  état, pastille réduite à un point. Pastilles (`NavItem.SetBadge`) : contrôles à corriger ou non vérifiés, pilotes plus
  récents, mise à jour d'OptiGame ; le nom accessible les dit (« Diagnostic, 3 points à corriger »). Cliquer « Mes jeux »
  déjà affiché revient à la grille. Chaque changement de page est annoncé (« Page Diagnostic »).
- **Partie en cours** : carte en pied (nom du jeu, « Optimisé depuis 20:15 », « Arrêter l'optimisation… » avec
  confirmation) ; bouton icône en barre compacte.
- **Alertes** (`Services/ShellAlerts`) : InfoBar en haut de chaque page, la plus grave d'abord, gardées pendant que la
  fenêtre est fermée. Détection des jeux en panne (non fermable tant qu'elle dure), restauration ratée au démarrage ou en fin
  de partie, reprise après plantage, partie optimisée partiellement, erreur d'interface ; « Ouvrir le journal » pour les
  erreurs. Mise à jour : même InfoBar (rouge si échec, barre de téléchargement, « Plus tard »).
- **Raccourcis** : Ctrl+1 … Ctrl+5 (pages, Paramètres en dernier), Ctrl+F (recherche de Mes jeux), F5 (actualiser la page :
  bibliothèques, analyse, pilotes ; rien ne s'écrit), Échap / Alt+← / bouton « précédent » de la souris (fiche → grille).
  Toute sortie d'une fiche modifiée passe par la garde « Abandonner les modifications ? » (retour, autre jeu depuis le dock,
  Quitter : `UnsavedChangesGuard`).
- **Fenêtre** : place et état agrandi gardés dans settings.json (`MainWindowPlacement`) ; à défaut, 1240 × 860 au plus et
  jamais plus de 90 % de la zone de travail (`Core/Settings/WindowLayout`, testé) ; place ignorée si sa barre de titre
  n'est plus visible. Première fermeture : un dialogue dit qu'OptiGame continue dans la zone de notification
  (« Continuer en arrière-plan » par défaut, ou « Quitter OptiGame »), une seule fois.

## Fiche du jeu

- Fil d'Ariane « Mes jeux › Nom » (Échap / Alt+← reviennent à la grille, avec la garde des modifications).
- Bannière à hauteur de son contenu (jamais rognée) : jaquette, nom (`Text.Hero`), temps de jeu ; **Jouer** (seule action
  principale) ou, pendant la partie, « Arrêter l'optimisation… » ; « Mesurer les FPS » ; menu « … » (Voir sur Steam,
  Épingler au dock si le dock est activé, Changer la jaquette…, Retirer de Mes jeux…).
- Trois onglets (`Segment`, flèches pour passer de l'un à l'autre), le dernier choisi est gardé :
  - **Vue d'ensemble** : note (chiffre + mot + « Note mesurée / estimée », « Calcul de la note… » tant qu'elle n'est pas
    calculée), « Mon réglage dans le jeu » (gardé tout de suite), puis temps de jeu, mesures et disque en cartes (2 colonnes,
    1 sous 720 de large ; `WidthToColumnsConverter`).
  - **Optimisation** : « Réglages de partie » (case « Optimiser ce jeu pendant les parties », réglages grisés sinon,
    avertissements en ligne, « Ce qui se passera » toujours visible) → barre **Enregistrer** ; « Réglages permanents (Windows
    et pilote) » → **Appliquer…** confirmé et **Restaurer l'original…**, état relu, jamais stocké.
  - **Propriétés** : nom (validé en ligne), fichier .exe surveillé, lancement, retrait du jeu (bouton Danger).
- Barre « Modifications non enregistrées » hors du défilement : « Abandonner les modifications » / « Enregistrer » (Ctrl+S) ;
  l'état modifié est une COMPARAISON avec la version enregistrée (revenir à l'original efface la barre), point sur l'onglet
  concerné et dans son nom accessible.

## Diagnostic et Pilotes

- **Diagnostic, une seule page** : verdict neutre pendant l'analyse (barre d'activité), puis icône de forme + titre
  (`DiagnosticVerdict` : Recommended, ManualActions, Incomplete, Ready — jamais « prêt » avec un contrôle non vérifié) ;
  une seule action principale « Appliquer les N optimisations… » ; bilan en InfoBar (succès, redémarrage), pas de dialogue
  modal pour un succès. « Optimisations actives » : « Restaurer l'original… » par ligne, « Tout restaurer… » ; les réglages
  propres à un jeu repliés à part. Contrôles par statut (À corriger · Non vérifié · À savoir · OK replié), ligne =
  icône de forme + titre + résumé (deux lignes, rien de tronqué) + statut en mot ; dépliage gardé d'une analyse à l'autre ;
  « Pourquoi » d'abord, puis les optimisations (bouton « Titre… » + exigences en badges), « Valeurs mesurées » repliées,
  options avancées en bouton Danger ; un contrôle non vérifié a « Réessayer » et ses « Détails techniques ».
- **Pilotes** : un modèle de carte (`InstallableDriverViewModel`) pour la carte graphique et le chipset — état en badge
  (icône + mot), versions « Installé » / « Disponible » groupées, « Installer le pilote X… » ; résultats précédents gardés
  (atténués) pendant une recherche ; bilan après l'installeur officiel (« pilote à jour » ou « installation non constatée ») ;
  Windows Update : consigne en InfoBar avant la liste, cases verrouillées pendant une installation, « Installer les 2 pilotes
  cochés… ».

## Mesures et Paramètres

- **Mesures** : page défilante. « Nouvelle mesure » (jeu choisi, en cours présélectionné ; « Autre programme » replié, dit
  prioritaire ; « Démarrer la mesure » ; statut en InfoBar typée + barre d'avancement). Liste compacte en deux lignes
  (libellé, badges « Automatique » / « Peu fiable », jeu · date · optimisé ; FPS et 1 % low alignés à droite ;
  `ListItem.Selectable` : jamais d'aplat vert), Suppr et menu « Supprimer… » ; les mesures automatiques arrivent sans
  redémarrer. Comparaison en tableau à en-têtes (colonnes partagées), écart = flèche + valeur + mot (« mieux », « moins
  bien », « stable ») + couleur, « Inverser ». Graphe de 260 de haut, légende dans sa carte, nom accessible = résumé.
- **Paramètres** : groupes Général · Mes jeux · Dock · Mesures · Mises à jour et à propos · Données, une
  `controls:SettingRow` par réglage (icône, titre, une phrase, contrôle à droite, « En savoir plus » replié) ; sous-réglages
  grisés quand le parent est désactivé ; PresentMon réglé ici (avertissement si la mesure auto est cochée sans lui) ;
  « Ouvrir le journal », « Quitter OptiGame… ». Tâche planifiée lue et écrite hors du thread de l'interface.

## Dialogues

Un seul langage : **aucune MessageBox Win32**, sauf le message bloquant d'`App.LoadStateFiles` (fichier d'état illisible),
affiché avant l'injection de dépendances. Tous les dialogues dérivent de `Dialogs/DialogWindow` (thème, hors barre des
tâches, centrés sur la fenêtre active, hauteur limitée à la zone de travail, `InitialFocus`) et utilisent
`controls:DialogLayout` : en-tête (`Icon` None/Info/Success/Warning/Error, `Heading` = titre niveau 1, `Description`),
corps défilant (`IsBodyScrollable=False` pour une liste qui défile seule), pied de boutons à droite. Ordre visuel = ordre de
tabulation.

API (`IDialogService`) :
- `Confirm(heading, message, confirmLabel, isDestructive)` : titre = la question (« Retirer Portal 2 de Mes jeux ? »),
  bouton = verbe de l'action (« Retirer le jeu », jamais « Oui »), `isDestructive` = bouton `DangerFilled` + icône
  d'avertissement. « Annuler » est le bouton par défaut (Entrée, Échap) et reçoit le focus.
- `ShowInfo(heading, message)` ; `ShowError(heading, message, details)` : titre = ce qui a échoué, message = conséquence
  et suite en clair, `details` = message technique (exception) replié dans « Détails techniques », copiable ; toujours écrit
  au journal.
- `ConfirmUndo(change)` : « Restaurer le réglage d'origine ? » / « Restaurer l'original ».
- Confirmations d'optimisation (`ConfirmChangeDialog`, `ConfirmChangesDialog`) : titre = l'optimisation, « Pourquoi »,
  avertissement en InfoBar, exigences, où la restaurer (fiche du jeu pour `game.*`, page Diagnostic sinon), « Détails
  techniques » repliés ; option avancée = case « J'ai lu l'avertissement… » obligatoire.
- `DriverInstallDialog` : la non-réversibilité et le chemin du retour en arrière en PREMIER (InfoBar), case « Je comprends… »
  obligatoire, point de restauration coché s'il est disponible.
- Pas de dialogue vide : un sélecteur sans rien à proposer est remplacé par un `ShowInfo` qui dit pourquoi.

Textes : `Core/Text/FrenchText` — `Count(n, "jeu", "jeux")` (vrai pluriel, 0 et 1 au singulier), `Agree`, `Typeset`
(espaces insécables avant `: ; ? ! %` et dans les « », appliqué par les dialogues).

## Glossaire (un terme par concept)

Celui de l'audit (§ 7), adopté le 2026-10-05 : **partie** (jamais « session »), **Arrêter l'optimisation et restaurer**,
**jeu** (pas « profil »), **réglages de partie**, **profil NVIDIA**, **contrôle**, **optimisation**, **réglage** /
**réglage d'origine**, **Appliquer…**, **Restaurer l'original…** / **Tout restaurer…**, **Annuler** = fermer sans rien faire
(uniquement), **Abandonner les modifications**, **Enregistrer**, **Non vérifié**, **mesure** (pas « capture »),
**magasin**, **bibliothèque**, **lanceur**, **fiche du jeu**, **Voir sur Steam**, **page** (jamais « onglet » pour une page),
**Rechercher dans Mes jeux**, **Détecter les jeux installés…**, **Actualiser** (F5), **Parcourir…**, **Supprimer…**,
**Voir les nouveautés**, **Droits administrateur** · **Redémarrage requis**, **FPS moyens** · **1 % les plus lents** ·
**Temps d'image P99**.

Typographie : espace fine insécable (U+202F) avant `: ; ? ! %` et dans les « » ; vrais pluriels (jamais « (s) ») ;
points de suspension sur toute commande qui ouvre une confirmation ou une saisie ; un seul format de date.
