# Politique de confidentialité d'OptiGame

*English version below.*

Dernière mise à jour : 7 octobre 2026.

## En bref

OptiGame **ne collecte aucune donnée** : pas de compte, pas de statistiques d'utilisation, pas de rapport de plantage envoyé,
pas de publicité. Rien n'est envoyé au développeur d'OptiGame.

## Ce qui reste sur votre PC

Vos profils de jeu, sauvegardes des réglages d'origine, mesures de FPS, temps de jeu, jaquettes, réglages et le journal
d'OptiGame sont enregistrés uniquement dans `%LocalAppData%\OptiGame` (Paramètres › Données › « Ouvrir le dossier »). Vos
identifiants IGDB, si vous en saisissez, y sont chiffrés avec la protection de données de Windows (DPAPI) et ne servent qu'à
IGDB. Désinstaller OptiGame garde ce dossier : supprimez-le pour tout effacer.

## Les services qu'OptiGame interroge

Pour certaines fonctions, OptiGame interroge des services tiers. Il ne leur envoie jamais d'information personnelle, mais
comme pour toute connexion à Internet, ces services voient votre adresse IP ; ils la traitent selon leur propre politique de
confidentialité.

| Service | Quand | Ce qui est envoyé |
|---|---|---|
| GitHub (`api.github.com`, `github.com`) | recherche de mise à jour : 2 min après le démarrage puis toutes les 24 h (réglable ou désactivable dans Paramètres › Mises à jour), téléchargement d'une mise à jour | la version d'OptiGame |
| Magasin Steam (`store.steampowered.com`) | estimation de la note d'un jeu Steam de votre bibliothèque ; description, avis des joueurs et note de la presse quand vous ouvrez la fiche d'un jeu Steam (au plus une fois par semaine et par jeu, jamais pendant une partie) | le numéro du jeu sur Steam |
| PCGamingWiki (`www.pcgamingwiki.com`) | estimation de la note d'un jeu hors Steam | le nom du jeu |
| IGDB / Twitch (`api.igdb.com`, `id.twitch.tv`, `images.igdb.com`) | jaquettes, fonds et genres, **seulement** si vous avez saisi vos identifiants IGDB | le nom des jeux, vos identifiants IGDB |
| Images Epic Games / GOG (`cdn1.epicgames.com`, `images.gog.com`) | jaquettes des jeux possédés non installés | l'adresse de l'image |
| Epic Games / GOG (`store-content.ak.epicgames.com`, `api.gog.com`) | bouton « Voir sur Epic Games / GOG » | l'identifiant du jeu dans le magasin |
| NVIDIA (`www.nvidia.com`, `gfwsl.geforce.com`, `*.download.nvidia.com`) | page « Pilotes » | le modèle de carte graphique et la version de Windows |
| AMD (`www.amd.com`, `drivers.amd.com`) | page « Pilotes » | le modèle de chipset |
| Windows Update (Microsoft) | page « Pilotes » | la recherche de pilotes est faite par Windows lui-même |

Les pages web (magasins, PCGamingWiki, cette politique) s'ouvrent dans votre navigateur, seulement quand vous cliquez.

Les programmes que vous lancez depuis OptiGame (Steam, Epic Games Launcher, GOG Galaxy, vos jeux) ont leurs propres
politiques de confidentialité.

## Contact

Questions ou remarques : [ouvrez un ticket sur GitHub](https://github.com/Antho69-jdr/OptiGame/issues).

---

# OptiGame privacy policy

Last updated: October 7, 2026.

**OptiGame does not collect any data**: no account, no usage statistics, no crash reports, no advertising. Nothing is sent to
the OptiGame developer.

This program will not transfer any information to other networked systems unless specifically requested by the user or the
person installing or operating it, except for the connections listed above, which never include personal information:
update checks on GitHub (can be turned off in Settings › Updates), game requirements lookups on the Steam store and
PCGamingWiki (Steam game number or game name), descriptions and review scores from the Steam store when a Steam game page is
opened (Steam game number), game artwork and genres from IGDB (only if the user enters IGDB credentials),
store artwork and store pages from Epic Games and GOG, and driver lookups from NVIDIA, AMD and Windows Update on the
"Drivers" page. As with any Internet connection, these services see your IP address and handle it under their own privacy
policies.

Your game profiles, backups of original settings, FPS measurements, playtime, artwork, settings and log are stored only on
your PC, in `%LocalAppData%\OptiGame`. IGDB credentials, if entered, are encrypted with Windows DPAPI.

Contact: [GitHub issues](https://github.com/Antho69-jdr/OptiGame/issues).
