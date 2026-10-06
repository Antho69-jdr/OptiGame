# OptiGame

Application Windows qui prépare le PC pour vos parties : plus de FPS, moins de saccades.

- **Diagnostic** du PC (mode Jeu, plan d'alimentation, boost du processeur, planification GPU, disque du jeu, pilotes…),
  avec des corrections expliquées et annulables.
- **Profils de jeu** : réglages appliqués au lancement d'un jeu (plan d'alimentation, programmes à fermer, priorité),
  puis rétablis à sa fermeture — même après un plantage.
- **Mes jeux** : bibliothèque Steam, Epic Games et GOG, lancement, temps de jeu, dock de raccourcis.
- **Mesures** des FPS avant / après avec [PresentMon](https://github.com/GameTechDev/PresentMon) (Intel, licence MIT).

Principes : toute modification est réversible, rien n'est appliqué sans votre accord, aucun réglage « placebo » ou risqué.

## Installation

Téléchargez `OptiGame-Setup-X.Y.Z.exe` depuis les [versions publiées](https://github.com/Antho69-jdr/OptiGame/releases/latest).
Windows 10 (1809) ou Windows 11, 64 bits. OptiGame se met ensuite à jour tout seul (réglable dans Paramètres › Mises à jour).

## Signature du code / Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

Signature gratuite fournie par SignPath.io, certificat de la SignPath Foundation. Sont signés : `OptiGame.exe`, les
bibliothèques d'OptiGame (`OptiGame.dll`, `OptiGame.Core.dll`, `OptiGame.Platform.dll`) et l'installeur. Ils sont fabriqués
par la [CI GitHub Actions](.github/workflows/installer.yml) de ce dépôt, sur les machines de GitHub, à partir du code publié
ici ; chaque signature est approuvée à la main. Les programmes tiers inclus gardent la signature de leur éditeur (PresentMon :
Intel ; .NET : Microsoft).

Rôles / Team roles :

- Auteurs et relecteurs (committers and reviewers) : [Antho69-jdr](https://github.com/Antho69-jdr)
- Approbateurs (approvers) : [Antho69-jdr](https://github.com/Antho69-jdr)

## Confidentialité / Privacy policy

OptiGame ne collecte aucune donnée : pas de compte, pas de statistiques d'utilisation, pas de publicité. Vos profils,
mesures et réglages restent sur votre PC (`%LocalAppData%\OptiGame`).

This program will not transfer any information to other networked systems unless specifically requested by the user or
the person installing or operating it, except for the connections listed below, which can be turned off or are only made
when the corresponding feature is used.

OptiGame se connecte seulement aux services suivants, sans jamais envoyer d'information personnelle :

| Service | Quand | Ce qui est envoyé |
|---|---|---|
| GitHub (`api.github.com`, `github.com`) | recherche de mise à jour : 2 min après le démarrage puis toutes les 24 h (désactivable dans Paramètres › Mises à jour) | la version d'OptiGame |
| Magasin Steam (`store.steampowered.com`) | estimation de la note d'un jeu Steam | le numéro du jeu sur Steam |
| PCGamingWiki (`www.pcgamingwiki.com`) | estimation de la note d'un jeu hors Steam | le nom du jeu |
| IGDB / Twitch (`api.igdb.com`, `id.twitch.tv`) | jaquettes et genres, **seulement** si vous avez saisi vos identifiants IGDB | le nom des jeux |
| Images Epic Games / GOG (`cdn1.epicgames.com`, `images.gog.com`) | jaquettes des jeux possédés non installés | l'adresse de l'image |
| Epic Games / GOG (`store-content.ak.epicgames.com`, `api.gog.com`) | bouton « Voir sur … » | l'identifiant du jeu dans le magasin |
| NVIDIA, AMD, Windows Update | page « Pilotes » | le modèle de carte graphique ou de chipset |

Vos identifiants IGDB sont chiffrés sur votre PC (DPAPI) et ne servent qu'à IGDB.

## Licence

[MIT](LICENSE). Composants tiers : voir [THIRD-PARTY-NOTICES](installer/THIRD-PARTY-NOTICES.txt).
