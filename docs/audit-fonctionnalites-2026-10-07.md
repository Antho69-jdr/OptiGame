# Audit des nouvelles fonctionnalités — 7 octobre 2026

Version de départ : OptiGame 1.9.0 (commit eb2e8b4, branches `main` et `refonte-ui`).
Aucune ligne d'OptiGame n'a été modifiée pour cet audit. Les maquettes du point 4 ont été faites dans une copie jetable du
code, puis supprimée ; les captures sont dans `artifacts\audit-2026-10-07\`, hors du dépôt, parce qu'elles montrent des
illustrations de jeux protégées par le droit d'auteur.

Dans tout le document :
- **Vérifié** = constaté le 2026-10-07 sur un vrai fichier, une vraie réponse ou un vrai essai sur la machine de développement.
- **Supposé** = déduit ou estimé, à confirmer avant de coder.

---

## 1. Tableau récapitulatif

| # | Fonctionnalité | Valeur | Effort | Risque | Recommandation |
|---|---|---|---|---|---|
| 4 | Fond de la fiche plus visible | Moyenne : l'appli paraît plus « jeu » | **Faible** (1 étape) | Faible | **Oui, en premier.** Maquette C ou A |
| 3 | Note des joueurs et de la presse, description, bande-annonce | **Haute** : la fiche devient une vraie page de jeu | Moyen (3 étapes) | Moyen : sources non officielles, nouvelles connexions | **Oui.** Steam d'abord, GOG ensuite, IGDB en recours |
| 2 | Réglages graphiques précis de chaque jeu | **Haute** : c'est le cœur d'OptiGame | Élevé, et il croît avec chaque jeu | Moyen : fichiers réécrits par les jeux, anti-triche | **Oui, en lecture d'abord**, écriture jeu par jeu après test avec vous |
| 6 | Personnalisation | Moyenne | Faible à moyen, selon le choix | Faible | **Oui, à la carte** (accent, taille des jaquettes, jeux récents dans la zone de notification) |
| 5 | Communauté sans serveur (partage de profils, de mesures) | Moyenne | Faible | Faible | **Oui pour la version sans serveur** |
| 5 | Communauté avec serveur (réglages et mesures partagés) | Haute en théorie | Très élevé | **Élevé** : collecte de données | **Non pour l'instant** : ce serait changer la nature du projet |
| 1 | Amis, chat textuel et vocal, version complète | Haute pour certains, nulle pour d'autres | **Très élevé** (des mois) + serveur à payer et à surveiller | **Très élevé** : RGPD, mineurs, modération | **Non**, sauf choix explicite de faire d'OptiGame une plateforme sociale |
| 1 | Version légère : salon vocal pair à pair par code d'invitation | Moyenne | Moyen (3 étapes) | Moyen | **À expérimenter seulement si vous y tenez** |
| 1 | Version minimale : ouvrir les amis Steam depuis OptiGame | Faible | Très faible | Aucun | Oui, si utile |

**Ordre proposé** (détail au § 10) : 4 → 3 → 2 en lecture → 6 → 5 sans serveur → 2 en écriture, jeu par jeu → 1 seulement
après une décision de fond.

---

## 2. Point 1 — Liste d'amis, chat textuel, chat vocal

### Ce que ça apporte
Retrouver ses amis dans OptiGame, voir qui joue à quoi, discuter et parler pendant la partie, sans autre programme.

### Ce que ça change pour le projet
Aujourd'hui, OptiGame **ne connaît personne** : pas de compte, aucune donnée envoyée (PRIVACY.md). Une liste d'amis demande
au minimum :
1. une **identité** (qui est qui) ;
2. un **point de rendez-vous** pour que deux PC se trouvent sur Internet (ils ne connaissent pas leurs adresses respectives,
   et la plupart des box bloquent les connexions entrantes) ;
3. pour la voix, souvent un **relais** quand les deux box refusent le contact direct.

Ces trois éléments supposent un serveur. Quelqu'un doit l'héberger, le payer, le mettre à jour, le surveiller et répondre
des données qui y passent.

### Les options réalistes

**Option A — Service complet hébergé par vous** (comptes, amis, présence, messages, voix relayée)
- Effort : **très élevé**. Serveur de comptes et de messages, relais vocal, chiffrement de bout en bout, blocage,
  signalement, suppression de compte, sauvegardes, sécurité. Plusieurs mois de travail, puis une maintenance permanente.
- Coût : petit serveur estimé entre 10 et 30 € par mois au départ, plus le trafic du relais vocal (*supposé* : un appel
  à deux relayé fait passer de l'ordre de 30 à 60 Mo par heure). Surtout, il faut du **temps d'administration** : pannes,
  mises à jour de sécurité, abus.
- Droit (*à faire valider par un juriste, non vérifié par moi*) :
  - **RGPD** : vous devenez responsable du traitement (registre, durées de conservation, droit d'accès et d'effacement,
    contrat avec l'hébergeur, hébergement dans l'UE de préférence, notification d'une fuite sous 72 h).
  - **Mineurs** : en France, un mineur de moins de 15 ans ne peut pas consentir seul au traitement de ses données
    (consentement conjoint avec un parent). Beaucoup de joueurs ont moins de 15 ans.
  - **Modération** : il faut un moyen de bloquer et de signaler, et des conditions d'utilisation. Le règlement européen sur
    les services numériques exclut la messagerie privée de la définition des « plateformes en ligne » (considérant 14),
    mais tout salon ouvert au public y entrerait.
  - **Chiffrement** : en France, fournir un moyen de chiffrement peut demander une déclaration à l'ANSSI. Il existe des
    exemptions pour les produits grand public. À vérifier.
- Principes : OptiGame passerait de « aucune donnée » à « données personnelles et contenus privés ». PRIVACY.md serait
  entièrement réécrit, et la promesse actuelle du projet disparaîtrait.

**Option B — OptiGame client d'un protocole de messagerie ouvert et fédéré**

Le principe est celui du courriel : chacun crée son compte sur le serveur public de son choix, et OptiGame n'est qu'un
client.
- Avantages : vous n'hébergez rien, et la modération ainsi que les données relèvent de l'opérateur du serveur choisi. Le
  chiffrement de bout en bout et la voix existent déjà dans ces protocoles.
- Inconvénients : effort **élevé**, car ces protocoles sont complexes (chiffrement, synchronisation, appels). L'utilisateur
  doit créer un compte ailleurs. Une partie de la valeur est perdue, puisque vos amis doivent avoir un compte compatible.
- PRIVACY.md : nouvelle connexion vers le serveur choisi par l'utilisateur ; OptiGame ne collecte toujours rien lui-même.

**Option C — Version légère, sans serveur : salon pair à pair par code d'invitation**
- Fonctionnement : vous cliquez sur « Créer un salon », OptiGame affiche un code. Vous l'envoyez à un ami par n'importe quel
  moyen, il le colle et vous renvoie son code de réponse. La connexion est alors directe entre les deux PC, pour le texte
  et la voix, chiffrée par le standard du web WebRTC.
- **Vérifié** : le moteur web de Windows (WebView2, version 154, présent sur la machine même si le navigateur Edge est
  retiré par AtlasOS) sait établir une connexion WebRTC **sans aucun serveur** : un canal de données s'est ouvert et a
  transmis un message entre deux connexions, avec un code de 458 caractères échangé à la main. Le codec vocal Opus est
  disponible.
- **Supposé** : sans aucun serveur, la connexion n'aboutit qu'en réseau local (ou en IPv6 ouvert). Pour traverser les box,
  il faut interroger un serveur public de découverte d'adresse (il voit votre adresse IP, à ajouter à PRIVACY.md). Même
  ainsi, une partie des connexions échouera (box dites « symétriques », opérateurs qui partagent une adresse IP entre
  plusieurs clients) faute de relais. J'estime ces échecs à 10 à 20 %, sans l'avoir mesuré.
- **Supposé** : le micro demande une page web « sécurisée ». La page de test (chargée depuis le texte) n'avait pas accès au
  micro ; il faudra servir la page sous une adresse locale (fonction prévue par WebView2), à vérifier.
- Limites : 2 à 4 personnes, aucune liste d'amis persistante ni présence (« en ligne »), échange de codes un peu technique,
  pas de messages hors ligne.
- Droit et vie privée : pas de compte, rien ne passe par vous, rien n'est stocké. L'ami voit votre adresse IP, ce qui est
  inhérent au pair à pair et doit être dit.
- Principes : léger seulement si le moteur web est créé à la demande puis fermé. Garder la voix **pendant** une partie
  contredit l'allègement actuel (fenêtre fermée pendant le jeu) : ce serait un choix explicite de l'utilisateur.
  Appuyer-pour-parler dans un jeu en plein écran demande un raccourci clavier global, qui peut entrer en conflit avec le
  jeu.

**Option D — Minimale : s'appuyer sur Steam**
- « Amis Steam » dans OptiGame ouvre la liste d'amis et le chat de Steam (adresse `steam://open/friends`, *supposé*
  fonctionnel comme les autres adresses steam:// déjà utilisées). Effort quasi nul, aucun risque.
- **Vérifié** : Steam garde en local, dans `localconfig.vdf`, une section « friends » de **319 entrées** (nom, historique
  des pseudos, avatar). Ce n'est **pas** une liste d'amis fiable : elle contient aussi des personnes croisées
  (*supposé*), sans présence ni jeu en cours. Je la déconseille : ce serait afficher des données de tiers sans garantie.
  La présence demande l'API web de Steam, avec une clé et un profil public : à écarter.

### Effort, en étapes livrables
- D : 1 petite étape.
- C : (1) salon texte par code, en réseau local ; (2) traversée des box et serveur de découverte public, PRIVACY.md ;
  (3) voix, micro, appuyer-pour-parler ; puis mesure de la mémoire et du processeur pendant une partie.
- A ou B : un projet à part entière, à découper si vous le décidez.

### Décisions à prendre
1. OptiGame doit-il rester « sans compte, sans données » ? Si oui, seules C et D sont possibles.
2. Pour C : acceptez-vous un serveur public de découverte d'adresse (adresse IP visible par lui) et 10 à 20 % de connexions
   impossibles ?
3. La voix doit-elle fonctionner **pendant** les parties, malgré le coût en mémoire ?

---

## 3. Point 2 — Réglages graphiques précis de chaque jeu

### Ce que ça apporte
Voir, puis régler, **chaque** option graphique d'un jeu (ombres, textures, V-Sync, limite de FPS, upscaling…), et donner
des conseils précis : « V-Sync est activée dans Overwatch : c'est elle qui plafonne vos FPS ».

### Existant
Unreal Engine : lecture et écriture réversible de la qualité (`GameUserSettings.ini`, journal `ini-value`). Unity : écran
seulement (registre).

### Relevé des vrais fichiers (vérifié le 2026-10-07, en lecture seule)

| Jeu | Fichier | Format | Ce qu'on y lit | Particularités |
|---|---|---|---|---|
| Overwatch | `Documents\Overwatch\Settings\Settings_v0.ini` | INI, valeurs entre guillemets | `[Render.13]` : DirectionalShadowDetail 4, EffectsQuality 4, ModelQuality 4, LocalFogDetail 4, SSAODetail 3, SSLRDetailLevel 3, **VerticalSyncEnabled 1**, FullScreenRefresh 165, WindowMode 1, HDR 1, HighQualityUpsample 3 ; `[GPU.6]` GPUScaler 2.0 | Sections **numérotées par version** (`Render.13`, `GPU.6`) : la version peut changer avec une mise à jour du jeu. Pas de résolution ni de limite de FPS dans ce fichier |
| Scrap Mechanic | `%AppData%\Axolot Games\Scrap Mechanic\User\User_<SteamID64>\settings.json` | JSON (tabulations) | TextureQuality 1, ShadowResolution 2, DrawDistance 2, Foliage 2, SSAO 2, ReflectionQuality 2, ShaderQuality 2, ParticleQuality 2, CloudQuality 2, AA 5, FXAA 1, FsrQuality 0, **VerticalSync 1**, FrameRateCap 165, 3440×1440, DisplayMode 1 | Réglages audio, langue et souris dans le même fichier ; `GraphicsSettingVersion` « 8017_12_2 » ; nombres écrits « 0.800000012 » (à recopier tels quels) |
| Star Citizen | `LIVE\user\client\0\Profiles\default\attributes.xml` (dossier du jeu) | XML `<Attr name value>`, `Version="35"` | SysSpec 1, SysSpec_* par groupe (ombres, textures, particules…), Upscaling 2, UpscalingModel 1, WindowMode 2, 3440×1440, HDR 1, MotionBlur 0 | Dossier d'un **anti-triche** dans le jeu. Fichier daté du 29 sept. |
| Portal 2 | `steamapps\common\Portal 2\update\cfg\video.txt` | KeyValues de Valve (déjà lu ailleurs par OptiGame) | cpu_level 2, gpu_level 3, mat_antialias 8, mat_forceaniso 16, **mat_vsync 1**, mat_queue_mode -1, 3440×1440, fullscreen 0 + nowindowborder 1 (plein écran fenêtré) | Pas dans `portal2\cfg` (où se trouve config.cfg). Steam Cloud synchronise `config.cfg` (userdata\…\620\remote), pas `video.txt` |
| Void Crew | `%UserProfile%\AppData\LocalLow\Hutlihut Games ApS\Void Crew\Settings.json` | JSON indenté | SettingsPreset 3, **VSync 2**, TargetFramerate 120, ScreenMode 1, ModelDetail 2, TextureQuality 2, ShadowQuality 2, VfxQuality 2, VolumetricRendering 2, AntiAlias 3, UpscaleKind « DLSS », UpscaleQuality 1 | Contient aussi le **nom du micro** et du casque : ne jamais le journaliser. Le dossier `LocalLow\<éditeur>\<jeu>` se déduit de `app.info`, déjà lu par OptiGame |

Constats :
- Il suffit de **4 formats** pour couvrir les 5 jeux : INI (Overwatch, comme Unreal), JSON (Scrap Mechanic, Void Crew), XML
  à attributs (Star Citizen) et KeyValues (Portal 2, Source).
- Les **chiffres ne disent pas leur sens** : « 4 » dans Overwatch, c'est Épique ou Ultra ? Le seul moyen fiable est de
  changer le réglage dans le menu du jeu et de regarder le fichier, comme on l'a fait pour PUBG. Cela demande une courte
  séance avec vous pour chaque jeu.
- **PCGamingWiki** (vérifié par l'API, déjà utilisée) donne le dossier des réglages pour 4 jeux sur 5, rarement le fichier
  exact (Scrap Mechanic : dossier seulement ; Void Crew : vide) et jamais le sens des valeurs. Il est utile pour **moi**,
  quand j'ajoute un jeu, mais pas à l'exécution.

### Options
1. **Un adaptateur codé par jeu** : simple au début, mais chaque jeu ajouté demande du code, et une nouvelle version d'OptiGame
   pour chaque correction.
2. **Base de définitions décrite par des données (recommandé)** : 4 lecteurs de format écrits une fois, puis un fichier de
   définitions par jeu. Il donne l'emplacement (avec des repères comme `{Documents}`, `{AppData}`, `{LocalLow}`, `{GameDir}`
   ou `{SteamID64}`), le format, les clés, le sens de chaque valeur dans les mots du jeu, le niveau Bas/Moyen/Élevé/Ultra
   et une mention « écriture autorisée oui/non ». Ajouter un jeu ne demande alors plus de code. Plus tard, d'autres
   personnes pourraient en proposer (voir point 5).
3. Lecture générique « au hasard » de tout fichier de réglages : à écarter. Le sens des valeurs serait deviné, ce qui est
   contraire à la règle de n'écrire jamais rien d'approximatif.

### Écrire sans risque
- Même méthode que pour Unreal : seule la ligne ou la valeur change, même encodage, fichier remplacé d'un coup, journal
  `fixes.json` (« game.* »), « Restaurer l'original… ». Il faudra un accesseur réversible par format, comme `ini-value`
  aujourd'hui (JSON, attribut XML, KeyValues).
- **Jeu fermé obligatoire** : ces jeux réécrivent leur fichier en quittant (*supposé* pour tous, constaté pour Unreal).
- Uniquement des **valeurs que le menu du jeu propose**, jamais de réglage caché.
- **Anti-triche** (*supposé*) : ces fichiers sont ceux que le jeu écrit lui-même depuis son menu, pas des fichiers du jeu.
  Les modifier revient à changer une option, sans risque connu. Prudence quand même pour **Star Citizen** : lecture seule
  tant qu'on ne l'a pas testé en vrai avec vous. Ne jamais toucher aux fichiers de commandes du moteur que certains joueurs
  ajoutent à la main.
- Overwatch : refuser l'écriture si le numéro de section a changé (`Render.14`…) ; la lecture continue.
- **Supposé** : certains jeux gardent une copie des réglages en ligne. Il faudra vérifier, jeu par jeu, qu'une valeur
  écrite survit au lancement suivant.

### Effort, en étapes livrables
1. **Lecture** des 5 jeux (base de définitions + 4 lecteurs), affichée dans la carte « Mon réglage dans le jeu » et dans un
   tableau « Réglages du jeu » de l'onglet Optimisation. Conseils précis : V-Sync, limite de FPS, upscaling.
2. **Sens des valeurs** vérifié avec vous, jeu par jeu, dans le menu de chaque jeu (une courte séance par jeu).
3. **Écriture réversible**, jeu par jeu, en commençant par les deux formats JSON (Void Crew, Scrap Mechanic) puis
   Overwatch et Portal 2. Star Citizen en dernier, s'il est retenu.

### Décisions à prendre
1. Lecture seulement d'abord, ou lecture et écriture dès le départ ?
2. Quels jeux en premier ? Et Star Citizen : en lecture seule définitivement ?
3. Êtes-vous d'accord pour une séance de test dans chaque jeu (changer une option, je regarde le fichier) ?

---

## 4. Point 3 — Notes, description et bande-annonce sur la fiche d'un jeu

### Ce que ça apporte
Une fiche qui présente le jeu : de quoi il parle, ce qu'en pensent les joueurs et la presse, une bande-annonce. Utile
surtout pour les jeux possédés mais non installés.

### Sources vérifiées le 2026-10-07

| Source | Ce qu'elle donne (réel) | Langue | Conditions |
|---|---|---|---|
| Magasin Steam, détails (`store.steampowered.com/api/appdetails?appids=<id>&l=french`, déjà contacté pour la configuration requise) | `short_description`, `about_the_game` (HTML), `metacritic` (Portal 2 : 95, avec lien ; **absent** pour ARC Raiders), `recommendations`, `movies` (Portal 2 : 18, ARC Raiders : 4), captures, `legal_notice` | **Français** (« L'initiative de tests perpétuels… ») | Service non documenté (peut changer sans préavis). Les conditions de l'API web de Steam (2010) autorisent l'affichage pour l'usage personnel, « tel quel », et seulement à la demande de l'utilisateur |
| Avis Steam (`store.steampowered.com/appreviews/<id>?json=1&language=all&num_per_page=0&l=french`) | Portal 2 : 461 815 positifs sur 467 868, « Overwhelmingly Positive » ; avec `l=french`, ARC Raiders : « très positives », 341 874 sur 418 369 | Français avec `l=french` | Nouvelle adresse sur le même domaine ; non documenté |
| IGDB (déjà intégré, vos identifiants) | `summary`, `storyline`, `rating` (joueurs IGDB : Portal 2 = 91,3 sur 4 498 votes), `aggregated_rating` (presse : 92,4 sur 9 critiques ; ARC Raiders : 89,3 sur 4 ; **Void Crew : 76 sur 1 seule critique**), vidéos = identifiants **YouTube** | **Anglais seulement** | Gratuit, usage commercial autorisé (documentation IGDB lue le 2026-10-07) ; chaque utilisateur utilise ses propres identifiants |
| GOG (`api.gog.com/products/<id>?expand=description,videos&locale=fr-FR`, déjà contacté pour « Voir sur GOG ») | Description **en français** (« The Witcher est un jeu de rôles… ») ; vidéos hébergées par un service vidéo tiers (lecteur intégré) | Français | Non documenté |
| Note GOG (`reviews.gog.com/v1/products/<id>/averageRating?reviewer=verified_owner`) | `{"value":4.4,"count":3367}` | — | Non documenté, nouveau domaine |
| Epic | Aucune source simple trouvée pour la description et les notes | — | Recours : IGDB, en anglais |

### Bandes-annonces : le point technique décisif
- **Vérifié** : Steam ne fournit plus de fichier vidéo simple pour les bandes-annonces récentes. Seuls des flux découpés
  existent (HLS et DASH : 1080p à 360p, piste audio séparée). Les anciennes adresses `movie480.mp4` répondent encore pour
  d'anciennes vidéos (Portal 2) et donnent 404 pour les récentes (ARC Raiders) : à écarter.
- Le lecteur vidéo de base de WPF ne lit pas ces flux (*supposé*, d'après ses formats connus).
- **Vérifié** : le moteur web WebView2 du PC (version 154) **lit nativement** le flux HLS de Steam (« lecture OK, 640×360 »),
  sans aucune bibliothèque ajoutée. Il lit aussi les morceaux en H.264 par l'autre méthode (MSE), au cas où.
- WebView2 fait partie de Windows 11, et AtlasOS l'a gardé. Il faut l'ajouter comme composant à OptiGame (paquet NuGet
  officiel de Microsoft). Son coût en mémoire (*supposé* : 100 à 200 Mo pendant la lecture, dans des processus séparés)
  impose de le créer **au clic sur « Lire »** et de le détruire en quittant la fiche, et de ne jamais le créer pendant une
  partie.
- Vidéos IGDB = YouTube : soit un lecteur YouTube intégré (nouvelle connexion, traceurs possibles même en mode « sans
  cookies »), soit, recommandé, un simple « Voir sur YouTube » qui ouvre le navigateur.

### Recommandation
- Fiche : un bloc « À propos » (description courte, « Lire la suite » repliable), « Avis des joueurs » (« Très positives,
  82 % sur 418 369 avis Steam »), « Presse » quand elle existe, puis une vignette de bande-annonce.
- Priorité des sources : Steam (en français) → GOG (en français) → IGDB (en anglais, marqué « en anglais »).
- **Ne jamais afficher une note presse fondée sur une seule critique** (Void Crew) : seuil à fixer, par exemple 3 critiques.
- Toujours citer la source et donner un lien vers sa page.
- Cache local de 7 jours (`store-details.json`) ; rien n'est demandé pendant une partie (GameTimeGate) ; images et vidéo
  seulement quand la fiche est ouverte.
- Le HTML de `about_the_game` contient des images et des mises en forme : on n'en garde que le texte. Aucun HTML n'est
  affiché hors du lecteur vidéo.

### Vie privée
Nouvelles connexions à ajouter à PRIVACY.md : `store.steampowered.com/appreviews`, les serveurs vidéo de Steam
(`video.akamai.steamstatic.com`, contacté seulement au clic sur « Lire »), `reviews.gog.com`, et éventuellement les vidéos
GOG et YouTube. Rien de personnel n'est envoyé : le numéro du jeu ou son nom. Comme toujours, ces services voient
l'adresse IP.

### Effort, en étapes livrables
1. Description et notes Steam (joueurs + presse), en français, avec cache et PRIVACY.md.
2. Bande-annonce Steam avec WebView2 à la demande (mesure de la mémoire avant et après), vérifiée en vrai.
3. GOG (description, note) et recours IGDB pour les autres jeux.

### Décisions à prendre
1. Acceptez-vous d'ajouter WebView2 (composant de Windows) pour lire les bandes-annonces dans OptiGame ? Sinon : un bouton
   qui ouvre la page Steam.
2. Vidéos YouTube d'IGDB : lecteur intégré, ou lien vers le navigateur (recommandé) ?
3. Textes en anglais d'IGDB : les afficher (marqués « en anglais ») ou les masquer ?

---

## 5. Point 4 — Fond de la fiche plus visible

### Existant
Bannière à la hauteur de son contenu (environ 270 unités), image en haut, voile `Brush.BannerFade` (au moins 70 % sous le
titre, pour garder un texte lisible même sur une image blanche).

### Trois maquettes (captures ui-snapshots, ARC Raiders, 1240 × 860)
Images dans `artifacts\audit-2026-10-07\` : `fond-0-actuel.png`, puis :

| Maquette | Principe | Avantages | Limites |
|---|---|---|---|
| **A — Grande bannière** (`fond-A-grande-banniere.png`, `…-880.png`) | Bannière plus haute (440 dans la maquette), voile léger en haut, foncé en bas où se trouve le texte ; jaquette gardée | Changement minimal, l'image prend presque deux fois plus de place | À 880 × 600, elle occupe tout le premier écran : la hauteur devra suivre la fenêtre (par exemple 45 % de la hauteur visible), pas une valeur fixe |
| **B — Fond de page fixe** (`fond-B-fond-de-page.png`) | L'image passe derrière **toute** la fiche, fil d'Ariane compris, et reste fixe quand on fait défiler ; elle se fond dans le fond de la fenêtre sous les cartes | La plus immersive | Image très agrandie, donc parfois rognée (visage coupé) ; les cartes restent opaques pour le contraste |
| **C — Plein cadre, voile latéral** (`fond-C-plein-cadre.png`, `…-880.png`, `…-star-citizen.png`) | L'image commence tout en haut, derrière le fil d'Ariane ; voile à **gauche** sous le texte, image **sans voile** à droite ; jaquette retirée | L'image est la plus visible et la plus nette ; le texte reste lisible (voile de 78 à 95 % sous le texte) ; marche aussi avec une illustration IGDB (Star Citizen) | La jaquette disparaît de la fiche (on peut la garder en option) |

Les maquettes respectent le système de design. Les couleurs sont des jetons ajoutés au thème (`Brush.HeroFadeBottom`,
`HeroFadeSide`, `HeroFadeEdge`, `HeroFadePage`) et aucune valeur n'est écrite dans la vue. Les contrastes seront recalculés
à l'implémentation et ajoutés à `docs/design-system.md`.

### Recommandation
**C**, avec la jaquette remise en option si vous y tenez. Sinon **A**, avec une hauteur proportionnelle à la fenêtre. Effort :
une étape. Aucun risque pour les principes.

### Décision à prendre
A, B ou C ? Faut-il garder la jaquette sur la fiche ?

---

## 6. Point 5 — Fonctionnalités communautaires

Toute fonction qui **rassemble** des données de plusieurs utilisateurs demande un serveur et une collecte, contraire à la
promesse actuelle. Je distingue donc ce qui marche sans serveur.

### Sans serveur (recommandé)
| Idée | Ce que c'est | Effort | Risque |
|---|---|---|---|
| **Partager un profil de jeu** | « Exporter… » crée un fichier `.optigame` (réglages de partie, programmes à fermer, plafond de FPS, qualité). « Importer… » le relit, le **valide** (processus protégés, chemins) et l'affiche avant tout enregistrement ; rien n'est appliqué | Faible | Faible : fichier venu d'ailleurs, donc validation stricte et jamais d'application automatique (principe 2) |
| **Partager une mesure** | « Copier le résumé » : « Portal 2 · RTX 3070 · 3440×1440 · Élevé · 144 FPS moyens · 1 % les plus lents : 98 », ou une image de la carte, pour l'envoyer où l'on veut | Faible | Aucun : c'est l'utilisateur qui partage |
| **Définitions de jeux enrichies par la communauté** | Les définitions du point 2 sont des fichiers de données dans le dépôt public : n'importe qui peut en proposer une sur GitHub, vous la relisez, elle arrive avec la mise à jour suivante | Faible, une fois le point 2 fait | Faible : tout est relu avant publication ; aucune connexion nouvelle |

### Avec serveur (déconseillé pour l'instant)
| Idée | Problème |
|---|---|
| Comparer ses FPS à ceux des PC semblables | Collecte de mesures et de configurations. Une combinaison de composants peut identifier une personne : il faudrait des seuils d'anonymat (pas de chiffre affiché sous N contributions). Il faut aussi se protéger contre les fausses données, et un serveur à payer |
| Réglages recommandés par la communauté, par jeu et par carte | Mêmes problèmes, plus de la modération |
| Profils publics et classement | Comptes, modération, mineurs : on retombe sur le point 1 |

### Décision à prendre
Rester sans serveur ? Si oui, lesquelles des trois idées sans serveur ?

---

## 7. Point 6 — Personnalisation

| Idée | Effort | Remarques |
|---|---|---|
| **Couleur d'accent** au choix (6 teintes) | Moyen | Le vert sert de marque, d'action et de sélection. Chaque teinte doit garder un contraste suffisant avec le texte posé dessus : je propose 6 teintes calculées d'avance plutôt qu'un nuancier libre. Changement appliqué au prochain démarrage (le thème est lu une fois) ou à chaud au prix d'une conversion plus large |
| **Thème clair** | Élevé | Tous les jetons à doubler, voiles sur images et contrastes à recalculer. Valeur moyenne pour une appli de jeu |
| **Taille des jaquettes** (petite / moyenne / grande) dans Mes jeux | Faible à moyen | Le décodage des jaquettes dépend de leur taille (ImageLoader, 198) : à adapter, mémoire à remesurer |
| **Vue liste** compacte dans Mes jeux | Moyen | Utile avec beaucoup de jeux |
| **Collections personnelles** (« Coop avec Max », « À finir ») et **jeux masqués** | Moyen | Filtre de plus, à côté des genres |
| **Page de démarrage** au choix (Mes jeux, Diagnostic…) | Très faible | — |
| **Jeux récents dans le menu de la zone de notification** (clic droit → Jouer) | Faible | L'instance tourne déjà : aucun écran d'élévation |
| Jeux récents dans la liste de raccourcis de la barre des tâches | Faible | **Supposé** : chaque clic relancerait l'exe, qui demande les droits administrateur, donc un écran d'élévation à chaque fois. Déconseillé tant que l'élévation n'est pas isolée |
| **Raccourcis clavier** personnalisables, raccourci global pour ouvrir OptiGame | Moyen | Raccourci global : risque de conflit avec les jeux, à proposer désactivé par défaut |
| **Ordre et affichage des pages** de la barre latérale (masquer Pilotes…) | Faible | — |
| **Dock** : styles du plateau (transparent, verre dépoli, couleur), taille du nom | Faible à moyen | L'aspect validé reste celui par défaut (coins 14 px) |
| **Fond de la fiche** : intensité du voile, flou | Faible | Avec le point 4 |
| Widgets du tableau de Windows 11 | — | **Supposé** : ils demandent une appli empaquetée (MSIX), ce qu'OptiGame n'est pas (Inno Setup). À écarter |

Recommandation : couleur d'accent, taille des jaquettes, jeux récents dans la zone de notification et page de démarrage.
Le meilleur rapport valeur/effort.

Décision à prendre : lesquelles ?

---

## 8. Ce qui a été vérifié, et comment

- Fichiers de réglages des 5 jeux, lus sur le disque (contenu au § 3). Aucun n'a été modifié.
- PCGamingWiki : `action=parse&prop=wikitext` sur Void Crew, Scrap Mechanic, Star Citizen, Overwatch 2 et Portal 2
  (modèle « Game data/config »).
- Steam : `appdetails` (Portal 2, ARC Raiders ; `l=french`), `appreviews` (avec et sans `l=french`), liste HLS et manifeste
  DASH d'une bande-annonce, anciennes adresses MP4 (404 pour les récentes).
- IGDB : requête multiple avec vos identifiants (lus, jamais écrits) : résumé, notes, vidéos de Portal 2, ARC Raiders et
  Void Crew. Documentation IGDB sur l'usage commercial.
- GOG : `api.gog.com/products/…?expand=description,videos&locale=fr-FR`, `reviews.gog.com/…/averageRating`.
- WebView2 : petit programme jetable, fenêtre hors écran sans prise de focus. Lecture HLS native, MSE H.264 et AV1, WebRTC
  sans serveur (canal de données), codecs audio. `getUserMedia` est indisponible sur une page chargée depuis le texte.
- Section « friends » de `localconfig.vdf` (compte des entrées seulement ; aucun nom recopié ici).
- Trello : aucune carte ouverte ne recoupe ces demandes.

## 9. Ce qui reste supposé (à confirmer avant de coder)

- Le sens des valeurs de chaque jeu (seul le menu du jeu le dit).
- Que chaque jeu réécrit son fichier en quittant, et qu'aucun ne remplace une valeur écrite par une copie en ligne.
- L'absence de risque anti-triche quand on modifie un fichier de réglages écrit par le jeu (prudence pour Star Citizen).
- La mémoire consommée par WebView2 pendant une lecture.
- Le taux d'échec des connexions pair à pair sans relais (10 à 20 %), l'accès au micro via une adresse locale sécurisée.
- Les points de droit du § 2 (RGPD, mineurs, règlement sur les services numériques, déclaration de chiffrement) : à faire
  valider par un juriste si l'option A ou B est retenue.
- Le comportement des listes de raccourcis de la barre des tâches avec un exe qui demande les droits administrateur.

## 10. Questions à trancher (récapitulatif)

1. **Nature du projet** : OptiGame reste-t-il « sans compte, sans données » ? (Cela décide des points 1 et 5.)
2. **Amis et chat** : rien, l'ouverture des amis Steam (D), un essai de salon pair à pair (C), ou un vrai service (A ou B) ?
3. **Réglages des jeux** : lecture seule d'abord ? Quels jeux en premier ? Star Citizen en lecture seule ? Séances de test
   avec vous ?
4. **Fiche du jeu** : WebView2 pour les bandes-annonces ? YouTube intégré ou en lien ? Textes anglais d'IGDB affichés ?
5. **Fond** : maquette A, B ou C ? Jaquette gardée ?
6. **Communauté sans serveur** : partage de profils, partage de mesures, définitions de jeux ouvertes aux contributions ?
7. **Personnalisation** : lesquelles ?

## 11. Ordre de réalisation proposé

Un commit par étape, votre test entre chaque :

1. **Fond de la fiche** (maquette choisie) : rapide, visible, sans risque.
2. **Description et notes Steam** sur la fiche (+ PRIVACY.md).
3. **Bande-annonce Steam** (WebView2 à la demande), puis **GOG et IGDB** en recours.
4. **Réglages des jeux, lecture** : base de définitions + 4 formats, les 5 jeux, conseils précis (V-Sync, limite de FPS).
5. **Personnalisation** retenue (accent, taille des jaquettes, jeux récents dans la zone de notification…).
6. **Partage sans serveur** (export/import de profil, résumé de mesure).
7. **Réglages des jeux, écriture réversible**, jeu par jeu, après une séance de test avec vous pour chacun.
8. **Amis et chat** : seulement après votre décision sur la nature du projet. Ouvrir les amis Steam peut se glisser à
   n'importe quel moment.

## 12. Décisions de l'utilisateur (7 octobre 2026)

1. **Identité** : un « compte » lié à Steam si possible, pour retrouver ses amis Steam. Sans serveur, ce compte est l'identité
   Steam déjà connue du PC ; la liste d'amis passe par l'API web de Steam avec une clé personnelle (comme IGDB), à vérifier
   en vrai à l'étape 8.
2. **Chat** : essayer le **pair à pair** (option C).
3. **Réglages des jeux** : **lecture seule** d'abord ; **Void Crew**, puis **Star Citizen**, puis les autres.
4. **Fiche** : la solution la plus légère et la plus fluide. Lecteur vidéo créé au clic seulement, détruit en quittant la
   fiche, jamais pendant une partie ; vidéos YouTube dans le navigateur.
5. **Fond** : maquette **C**, **jaquette gardée**.
6. **Communauté** : sans serveur, si le pair à pair suffit.
7. **Personnalisation** : accent, taille des jaquettes, jeux récents dans le menu de notification, page de démarrage.

Ordre du § 11 confirmé.
