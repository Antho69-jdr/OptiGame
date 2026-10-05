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

À venir dans la refonte (voir `docs/ui-ux-audit-2026-10-05.md`) : gabarit de dialogue thémé, ligne de réglage façon
Paramètres de Windows 11, liste à cocher, bouton fractionné, menu « … », CoverTile unique.

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
