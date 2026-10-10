# Politique de confidentialité d'OptiGame

*English version below.*

Dernière mise à jour : 9 octobre 2026.

## En bref

OptiGame **ne collecte aucune donnée** : pas de compte, pas de statistiques d'utilisation, pas de rapport de plantage envoyé,
pas de publicité. Rien n'est envoyé au développeur d'OptiGame, hormis ce qu'il faut à son serveur de mise en relation des
appels vocaux, le temps d'un appel ou de votre connexion à vos amis Steam (rien n'y est gardé ensuite, voir plus bas).

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
| Images et vidéos du magasin Steam (`shared.akamai.steamstatic.com`, `video.akamai.steamstatic.com`) | vignette de la bande-annonce à l'ouverture de la fiche d'un jeu Steam (téléchargée une fois) ; la vidéo **seulement** quand vous cliquez sur « Lire » | l'adresse de l'image ou de la vidéo |
| PCGamingWiki (`www.pcgamingwiki.com`) | estimation de la note d'un jeu hors Steam | le nom du jeu |
| IGDB / Twitch (`api.igdb.com`, `id.twitch.tv`, `images.igdb.com`) | jaquettes, fonds et genres, et présentation d'un jeu hors Steam et hors GOG quand vous ouvrez sa fiche (résumé, notes, liste de vidéos), **seulement** si vous avez saisi vos identifiants IGDB | le nom des jeux ou leur numéro sur IGDB, vos identifiants IGDB |
| Images Epic Games / GOG (`cdn1.epicgames.com`, `images.gog.com`) | jaquettes des jeux possédés non installés | l'adresse de l'image |
| Epic Games / GOG (`store-content.ak.epicgames.com`, `api.gog.com`, `reviews.gog.com`) | bouton « Voir sur Epic Games / GOG » ; description et note d'un jeu GOG quand vous ouvrez sa fiche (au plus une fois par semaine, jamais pendant une partie) | l'identifiant du jeu dans le magasin |
| NVIDIA (`www.nvidia.com`, `gfwsl.geforce.com`, `*.download.nvidia.com`) | page « Pilotes » | le modèle de carte graphique et la version de Windows |
| AMD (`www.amd.com`, `drivers.amd.com`) | page « Pilotes » | le modèle de chipset |
| Windows Update (Microsoft) | page « Pilotes » | la recherche de pilotes est faite par Windows lui-même |

Les pages web (magasins, critiques de la presse, bandes-annonces des jeux hors Steam, PCGamingWiki, cette politique) s'ouvrent dans votre navigateur, seulement quand
vous cliquez. Les bandes-annonces de Steam sont lues dans OptiGame par le moteur web de Windows (WebView2), créé au clic sur « Lire » et
fermé ensuite ; il ne charge que la vidéo de Steam, et son filtre de réputation (qui enverrait l'adresse de la page à Microsoft)
est désactivé.

Les programmes que vous lancez depuis OptiGame (Steam, Epic Games Launcher, GOG Galaxy, vos jeux) ont leurs propres
politiques de confidentialité.


## Appel vocal (page « Amis »)

L'appel passe **directement entre votre PC et celui de chaque participant** (WebRTC, voix chiffrée de bout en bout) : aucun
compte, rien n'est enregistré, la voix ne passe par aucun serveur. Seulement la voix : aucun texte n'est échangé.

- **Appel de groupe** (6 personnes au plus) : chaque participant est relié directement à chacun des autres. Chacun voit donc les
  adresses réseau des autres participants (comme dans un appel à deux), et chacun peut inviter un de ses amis.
- **Mise en relation** : pour se trouver, les PC passent par le serveur de mise en relation d'OptiGame
  (`optigame-call.antho-b-69.workers.dev`, hébergé chez Cloudflare, code source dans `server/call-relay`). Il reçoit le code de l'appel
  (6 caractères tirés au hasard), le nom affiché de chaque participant, et relaie entre les PC leurs descriptions de connexion :
  adresses réseau (locales et, si l'option ci-dessous est cochée, publique) et clés de chiffrement publiques. Chaque PC y reste
  connecté pendant l'appel (pour qu'un invité puisse arriver) ; la voix n'y passe jamais. Il ne garde rien : le salon est effacé
  dès que le dernier participant part, au bout de 2 minutes si personne ne rejoint, et au plus tard après 12 heures. Comme tout
  serveur, il voit l'adresse IP de connexion ; OptiGame n'y écrit aucun journal. Les **mots de contrôle** affichés sous le nom de
  chaque participant permettent de vérifier que personne (pas même ce serveur) ne s'est interposé.
- **Amis Steam** (facultatif, bouton « Se connecter avec Steam ») : la connexion se fait sur la page officielle de Steam, dans
  votre navigateur ; OptiGame ne voit jamais votre mot de passe. Steam confirme au serveur de mise en relation votre numéro de
  compte Steam (public), et le serveur remet à OptiGame un jeton de connexion valable 30 jours, gardé chiffré sur votre PC
  (protection de données de Windows). Tant qu'OptiGame est ouvert et que vous êtes « visible », il reste connecté au serveur, qui
  demande alors à Steam (`api.steampowered.com`, avec la clé d'API de l'auteur d'OptiGame) votre nom affiché et votre liste
  d'amis, et les garde **seulement le temps de cette connexion** (effacés dès qu'OptiGame se ferme ou que vous devenez
  invisible). Vous n'êtes visible, et joignable, que par vos **amis Steam** qui ont OptiGame ouvert, et par vos **amis OptiGame**
  (« Ajouter un ami… » : demande envoyée à une personne que votre client Steam connaît, acceptée par elle ; la liste de vos amis
  OptiGame est gardée sur votre PC et envoyée au serveur seulement le temps de la connexion). Une demande d'ami ou un appel
  transmet seulement votre nom affiché (et le code de l'appel). « Se déconnecter » efface le jeton de votre PC.
- **« Passer par Internet »** (décoché par défaut, à cocher quand votre ami n'est pas sur votre réseau) : OptiGame demande alors
  au serveur public de découverte d'adresse `stun.cloudflare.com` (protocole STUN) l'adresse sous laquelle Internet voit votre
  PC. Ce serveur voit votre adresse IP ; votre voix ne passe jamais par lui.
- OptiGame accède au micro dès que vous créez ou rejoignez un appel (le son reste coupé tant que vous ne l'ouvrez pas) et le
  libère au raccroché. Le moteur web de Windows (WebView2) qui fait l'appel n'existe que pendant l'appel.

## Contact

Questions ou remarques : [ouvrez un ticket sur GitHub](https://github.com/Antho69-jdr/OptiGame/issues).

---

# OptiGame privacy policy

Last updated: October 9, 2026.

**OptiGame does not collect any data**: no account, no usage statistics, no crash reports, no advertising. Nothing is sent to
the OptiGame developer, except what its voice-call matchmaking server needs during a call or while connected to Steam friends (nothing is kept afterwards, see below).

This program will not transfer any information to other networked systems unless specifically requested by the user or the
person installing or operating it, except for the connections listed above, which never include personal information:
update checks on GitHub (can be turned off in Settings › Updates), game requirements lookups on the Steam store and
PCGamingWiki (Steam game number or game name), descriptions and review scores from the Steam store when a Steam game page is
opened (Steam game number), trailer thumbnails and, only when the user clicks Play, trailer videos from Steam's media servers
(played by the Windows web engine, WebView2, closed afterwards), game artwork, genres and descriptions from IGDB (only if the user
enters IGDB credentials; game name or IGDB game number),
store artwork and store pages from Epic Games and GOG, GOG game descriptions and ratings when a GOG game page is opened (GOG
product number), and driver lookups from NVIDIA, AMD and Windows Update on the
"Drivers" page. As with any Internet connection, these services see your IP address and handle it under their own privacy
policies.

Voice calls ("Amis" page) go directly between the PCs of the participants (WebRTC, end-to-end encrypted voice), with no
account and no recording; the voice never goes through a server and no text is exchanged. In a group call (up to 6 people),
every participant is connected directly to every other one and therefore sees their network addresses. To find each other,
the PCs use OptiGame's matchmaking server (`optigame-call.antho-b-69.workers.dev`, hosted by Cloudflare, source in
`server/call-relay`), which relays their connection descriptions (network addresses, public encryption keys) and display names
under a random 6-character call code, stays connected during the call so that invited friends can join, and keeps nothing:
the room is erased when the last participant leaves, after 2 minutes if nobody joins, and after 12 hours at most. Only when the user ticks "Passer par
Internet" does OptiGame ask the public STUN server `stun.cloudflare.com` for the PC's public address (the server sees the IP
address, never the voice). Optional "Steam friends": the user signs in on Steam's official page in their browser (OptiGame never
sees the password); Steam confirms the public Steam account number to the matchmaking server, which gives OptiGame a 30-day
sign-in token kept encrypted on the PC. While OptiGame is open and the user is "visible", the server asks Steam
(`api.steampowered.com`, with the OptiGame author's API key) for the display name and friends list and keeps them only for
the duration of that connection; users are visible and callable only by their Steam friends who have OptiGame open.

Your game profiles, backups of original settings, FPS measurements, playtime, artwork, settings and log are stored only on
your PC, in `%LocalAppData%\OptiGame`. IGDB credentials, if entered, are encrypted with Windows DPAPI.

Contact: [GitHub issues](https://github.com/Antho69-jdr/OptiGame/issues).
