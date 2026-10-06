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

OptiGame utilise SignPath Foundation pour signer son code (signature gratuite pour les logiciels libres). Sont signés :
`OptiGame.exe`, les bibliothèques d'OptiGame (`OptiGame.dll`, `OptiGame.Core.dll`, `OptiGame.Platform.dll`) et l'installeur.
Ils sont fabriqués par la [CI GitHub Actions](.github/workflows/installer.yml) de ce dépôt, sur les machines de GitHub, à
partir du code publié ici, et chaque signature est approuvée à la main. Les programmes tiers inclus gardent la signature de
leur éditeur (PresentMon : Intel ; .NET : Microsoft) ; le désinstalleur (Inno Setup) n'est pas signé. La page de chaque
version indique si son installeur est signé : la signature est en cours de mise en place, les versions jusqu'à la 1.8.0 ne
le sont pas.

OptiGame uses SignPath Foundation for code signing. Signed files: `OptiGame.exe`, OptiGame's own libraries and the
installer, built from this repository by GitHub Actions on GitHub-hosted runners; every signing request is approved
manually. Releases up to 1.8.0 are not signed yet.

Rôles / Team roles :

- Auteurs et relecteurs (committers and reviewers) : [Antho69-jdr](https://github.com/Antho69-jdr)
- Approbateurs (approvers) : [Antho69-jdr](https://github.com/Antho69-jdr)

## Confidentialité / Privacy policy

OptiGame ne collecte aucune donnée : pas de compte, pas de statistiques d'utilisation, pas de publicité. Vos profils,
mesures et réglages restent sur votre PC. Pour certaines fonctions (mises à jour, configuration requise des jeux, jaquettes,
pilotes), il interroge quelques services, sans leur envoyer d'information personnelle : la
[politique de confidentialité](PRIVACY.md) dit lesquels, quand, et ce qui leur est envoyé.

OptiGame does not collect any data. See the [privacy policy](PRIVACY.md) for the few services it contacts and what is sent.

## Licence

[MIT](LICENSE). Composants tiers : voir [THIRD-PARTY-NOTICES](installer/THIRD-PARTY-NOTICES.txt).
