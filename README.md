# Game Booster

Application Windows pour préparer un PC au jeu : plan d'alimentation, mode jeu, captures, nettoyage, priorité des processus et tableau de bord. L'interface est en français. Le programme demande les droits administrateur, applique des réglages Windows réversibles et n'ouvre jamais la mémoire d'un jeu.

Les presets personnels, les préférences et l'historique de boost sont stockés dans Cloudflare D1, un compte par utilisateur. L'application ne conserve qu'un cache local chiffré avec DPAPI pour continuer hors ligne. La sauvegarde utilisée par **Restaurer par défaut** reste sur la machine : ce sont les valeurs Windows d'origine, propres à ce PC.

## Fonctionnalités

- Tableau de bord : processeur, mémoire, utilisation 3D du GPU et température NVIDIA si `nvidia-smi` est présent.
- Boost en un clic à partir d'un preset, avec journal.
- Presets intégrés : Compétitif / Fortnite, Équilibré, Streaming, Économie. Presets personnels créés dans l'application, import et export JSON (`gamebooster-presets`, version 1).
- Plan d'alimentation Performances ultimes, Performances élevées ou Utilisation normale. Si le mode ultime n'existe pas, le mode élevé est activé et le journal le dit.
- Fermeture d'une liste fermée d'applications (Discord, navigateurs, lanceurs web, etc.). Les processus système, anti-triche et le jeu lui-même sont refusés.
- Nettoyage des fichiers temporaires, des caches de shaders DirectX / NVIDIA / AMD, et de la corbeille.
- Mode jeu, HAGS, captures Xbox Game Bar, optimisations plein écran pour des exécutables choisis, effets visuels.
- Priorité **Haute** pour les jeux surveillés. Jamais Temps réel, pas d'affinité, pas de lecture mémoire.
- Nagle désactivé sur les interfaces qui ont une adresse, vidage DNS, test ICMP.
- Point de restauration avant les changements, et **Restaurer par défaut** pour réécrire les valeurs enregistrées la première fois.

## Ce que l'application ne fait pas

Elle n'injecte rien, ne lit pas la mémoire des jeux, n'écrit pas dans leurs dossiers et ne modifie pas les processus anti-triche (Easy Anti-Cheat, BattlEye, Vanguard, FACEIT). HAGS ne prend effet qu'après un redémarrage : l'application ne redémarre pas le PC. Le nettoyage et la corbeille sont définitifs : les restaurer ne ramène pas les fichiers.

Le ping affiché est une latence ICMP. Ce n'est pas le lag d'une partie. Celui-ci dépend du fournisseur d'accès, du routage et des serveurs du jeu.

## Télécharger

L'exécutable est autonome (`win-x64`, .NET 8 inclus).

1. Ouvrir les [releases](https://github.com/nuiraz/Fps/releases/latest) et prendre `GameBooster.exe` attaché à une étiquette `v*`.
2. Sans release, ouvrir les [Actions](https://github.com/nuiraz/Fps/actions), choisir un build vert et télécharger l'artefact **GameBooster-win-x64**.

Windows demande une élévation administrateur au lancement. L'application vise Windows 10 ou plus récent. Si un antivirus s'inquiète des changements de registre ou de la fermeture de processus, le code de ce dépôt est la référence.

## Comptes et synchronisation

Inscription et connexion avec un nom d'utilisateur et un mot de passe. Pas d'adresse e-mail. Le mot de passe est hashé sur le Worker (PBKDF2-SHA256, 100 000 itérations, sel aléatoire) et n'est jamais stocké en clair.

| Donnée | Où elle vit |
| --- | --- |
| Compte, sessions, presets personnels, préférences, historique | Cloudflare D1 |
| Cache de ces données pour l'usage hors ligne | `%AppData%\GameBooster\cache\<utilisateur>\profile.dat`, protégé par DPAPI |
| Jeton de session si « se souvenir de moi » | `%AppData%\GameBooster\session.dat`, DPAPI |
| URL de l'API et l'option « se souvenir de moi » | `%AppData%\GameBooster\machine-settings.json` |
| Valeurs à restaurer (registre, plan d'alimentation, surveillance de priorité) | `%AppData%\GameBooster\machine-backup.dat`, DPAPI, jamais envoyées |

La synchro a lieu à la connexion, après un changement, toutes les deux minutes, et avec le bouton **Synchroniser**. La dernière écriture gagne, d'après `updatedAt`. Un enregistrement plus ancien reçoit une réponse 409 et le client reprend la version du serveur. L'historique est ajouté seulement : le même identifiant n'est pas dupliqué. Les suppressions de presets laissent une marque pour se propager. Sans compte, un cache `_local` existe sur le PC et n'est pas versé dans le compte à la connexion.

L'URL par défaut est `https://fps-api.<subdomain>.workers.dev`. Tant que `<subdomain>` est présent, l'application prévient et n'appelle pas le serveur. Remplacez-la dans **Paramètres**.

## Compiler

Le projet est `src/GameBooster/GameBooster.csproj` (WPF, .NET 8, `win-x64`).

```powershell
dotnet publish src/GameBooster/GameBooster.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:IncludeAllContentForSelfExtract=true
```

L'exécutable est `src/GameBooster/bin/Release/net8.0-windows/win-x64/publish/GameBooster.exe`.

GitHub Actions (`.github/workflows/build.yml`) publie cet exécutable sur `windows-latest` et le joint à une release lors d'une étiquette `v*`. Le job `worker` vérifie le typecheck et les tests du Worker.

## Déployer Cloudflare

Rien n'est déployé par ce dépôt. Les commandes ci-dessous sont à lancer quand vous le décidez, depuis `cloud/` pour l'API et avec le dossier `site/` pour les pages. Il faut Wrangler et un compte Cloudflare.

```bash
cd cloud
npm ci
npx wrangler d1 create fps-auth
```

Copiez l'`database_id` renvoyé dans `cloud/wrangler.toml` à la place de `00000000-0000-4000-8000-000000000001`.

```bash
npx wrangler d1 migrations apply fps-auth --remote
npx wrangler secret put JWT_SECRET
npx wrangler deploy
npx wrangler pages deploy ../site --project-name fps-site
```

`JWT_SECRET` doit être une longue chaîne aléatoire (au moins 32 octets). Ne le commitez pas. Pour un essai local seulement, créez `cloud/.dev.vars` (déjà ignoré par git) :

```bash
JWT_SECRET=remplacez-par-une-longue-chaine-aleatoire
```

Puis `npx wrangler dev` dans `cloud/`. Le schéma D1 est `cloud/migrations/0001_init.sql` : utilisateurs, sessions, tentatives de connexion, presets, réglages, historique.

Mettez ensuite l'URL `https://fps-api.<votre-sous-domaine>.workers.dev` dans l'application (**Paramètres**) et dans `site/js/config.js` avant de republier les pages. Le lien de téléchargement du site pointe vers les releases GitHub.

### API

Toutes les réponses sont JSON. Les routes de données exigent `Authorization: Bearer`.

- `POST /api/register` et `POST /api/login` : `{ username, password, remember }`. Jeton 12 h, ou 30 jours si `remember` vaut `true`.
- `POST /api/logout`, `GET /api/me`, `GET /api/health`.
- `GET /api/presets?includeDeleted=1`, `PUT /api/presets/:id`, `DELETE /api/presets/:id`.
- `GET /api/settings`, `PUT /api/settings`.
- `GET /api/history?limit=200`, `POST /api/history` avec une entrée ou `{ entries: [...] }` (50 maximum).

La connexion est limitée à 8 échecs par adresse et 10 par nom sur 15 minutes. Le corps des requêtes est plafonné à 256 Ko. Les journaux du Worker sont du JSON structuré, sans mot de passe ni jeton.

### Limite CPU et PBKDF2

Le hachage est volontairement à 100 000 itérations. Sur le plan Workers gratuit, le temps CPU par requête est d'environ 10 ms : un login peut alors échouer avec l'erreur 1102. Le plan Workers payant (30 s de CPU par défaut) convient. Baisser `PBKDF2_ITERATIONS` dans `cloud/src/crypto.ts` affaiblit les mots de passe ; ne le faites que si vous acceptez ce compromis, et gardez la même valeur pour les comptes déjà créés (le nombre d'itérations est enregistré avec chaque hash).

## Configuration de l'URL d'API

Dans l'application : **Paramètres**, champ URL, **Enregistrer**, puis **Tester l'API**. Le test appelle `GET /api/health`. Cette adresse n'est pas synchronisée : chaque PC a la sienne.
