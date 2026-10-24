# Game Booster

Application Windows pour préparer un PC au jeu : plan d'alimentation, mode jeu, captures, nettoyage, priorité des processus et tableau de bord. L'interface est en français. Le programme demande les droits administrateur, applique des réglages Windows réversibles et n'ouvre jamais la mémoire d'un jeu.

Les presets personnels, les préférences et l'historique de boost sont stockés dans Cloudflare D1, un compte par utilisateur. L'application ne conserve qu'un cache local chiffré avec DPAPI pour continuer hors ligne. La sauvegarde utilisée par **Restaurer par défaut** reste sur la machine : ce sont les valeurs Windows d'origine, propres à ce PC.

## Fonctionnalités

- Tableau de bord : jauges circulaires CPU, RAM (Go utilisés), GPU (VRAM NVIDIA si `nvidia-smi` répond) et disque, températures quand elles sont lisibles, courbes CPU/RAM, score de performance qui monte après un boost, cartes mode jeu / plan d'alimentation / preset actif / ping ICMP, et bouton **BOOST** avec anneau de progression.
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

1. Télécharger [GameBooster.exe](https://github.com/nuiraz/Fps/releases/latest/download/GameBooster.exe). Ce lien est public : aucun compte GitHub n'est demandé. La [page des versions](https://github.com/nuiraz/Fps/releases/latest) liste aussi `GameBooster-win-x64.zip`, le même exécutable dans une archive.
2. Les artefacts Actions restent un secours pour un build sans étiquette `v*`. Ils demandent une connexion GitHub.

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

La synchro a lieu à la connexion, après un changement, toutes les deux minutes, et avec le bouton **Synchroniser**. La dernière écriture gagne, d'après `updatedAt`. Chaque modification locale reçoit une date strictement plus récente que la précédente, même si deux enregistrements tombent dans la même milliseconde. Un enregistrement plus ancien reçoit une réponse 409 et le client reprend la version du serveur. L'historique est ajouté seulement : le même identifiant n'est pas dupliqué. Les suppressions de presets laissent une marque pour se propager. Sans compte, un cache `_local` existe sur le PC et n'est pas versé dans le compte à la connexion.

Les presets personnels ont un identifiant UUID. `PUT /api/presets/{id}` envoie `createdAt` et `updatedAt` en ISO-8601 UTC. `DELETE /api/presets/{id}` envoie le même `updatedAt` dans le corps JSON et dans la query : le Worker lit d'abord le corps, puis `?updatedAt=` si le corps n'a pas de date. `PUT /api/settings` n'envoie que les champs acceptés (listes non nulles, hôte de ping, 1 à 10 pings, applications de la liste fermée, chemins `C:\…\jeu.exe`).

## Services en ligne

L'API et le site sont déjà déployés :

- API : https://fps-api.nathna.workers.dev
- Site : https://fps-site.nathna.workers.dev

L'application utilise cette API par défaut. Le site lit la même adresse dans `site/js/config.js`. `cloud/wrangler.toml` pointe vers la base D1 `fps-auth`, identifiant `01151840-bb57-428f-a25e-7abd0cf05311`.

Une installation neuve appelle cette API tout de suite. Un `machine-settings.json` déjà présent sur le PC conserve l'URL qu'il contient : si elle a encore `<subdomain>`, **Paramètres** sert à la remplacer. Tant que ce texte est là, l'application n'appelle pas le serveur.

## Compiler

Le projet est `src/GameBooster/GameBooster.csproj` (WPF, .NET 8, `win-x64`).

```powershell
dotnet publish src/GameBooster/GameBooster.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:IncludeAllContentForSelfExtract=true
```

L'exécutable est `src/GameBooster/bin/Release/net8.0-windows/win-x64/publish/GameBooster.exe`.

GitHub Actions (`.github/workflows/build.yml`) publie cet exécutable sur `windows-latest` et le joint à une release lors d'une étiquette `v*`. Le job `worker` vérifie le typecheck et les tests du Worker.

## Déployer Cloudflare

L'API `https://fps-api.nathna.workers.dev` et le site `https://fps-site.nathna.workers.dev` sont déjà en ligne. Ce dépôt ne relance pas le déploiement. Les commandes ci-dessous servent à republier plus tard, depuis `cloud/` pour l'API et avec le dossier `site/` pour les pages. Il faut Wrangler et le compte Cloudflare qui possède ces Workers.

```bash
cd cloud
npm ci
npx wrangler secret put JWT_SECRET
npx wrangler deploy
npx wrangler pages deploy ../site --project-name fps-site
```

`JWT_SECRET` doit être une longue chaîne aléatoire (au moins 32 octets). Ne le commitez pas. Pour un essai local seulement, créez `cloud/.dev.vars` (déjà ignoré par git) :

```bash
JWT_SECRET=remplacez-par-une-longue-chaine-aleatoire
```

Puis `npx wrangler dev` dans `cloud/`. Le schéma D1 est `cloud/migrations/0001_init.sql` : utilisateurs, sessions, tentatives de connexion, presets, réglages, historique.

La migration `0001_init.sql` a été appliquée à la main sur la base distante. La table de suivi `d1_migrations` ne la connaît donc pas. Avant le prochain `wrangler d1 migrations apply`, marquez-la comme déjà appliquée, sinon Wrangler rejoue les `CREATE TABLE` et échoue :

```bash
cd cloud
npx wrangler d1 execute fps-auth --remote --command "CREATE TABLE IF NOT EXISTS d1_migrations (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE, applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL);"
npx wrangler d1 execute fps-auth --remote --command "INSERT INTO d1_migrations (name) VALUES ('0001_init.sql');"
npx wrangler d1 migrations apply fps-auth --remote
```

Le nom inséré doit être exactement `0001_init.sql`. Si la ligne existe déjà, l'insertion échoue sur la contrainte d'unicité : passez alors directement à `migrations apply`.

`site/js/config.js` pointe déjà vers `https://fps-api.nathna.workers.dev`. Le bouton du site télécharge `https://github.com/nuiraz/Fps/releases/latest/download/GameBooster.exe`. Une étiquette `v*` publie une release complète (ni brouillon, ni préversion) avec `GameBooster.exe` et `GameBooster-win-x64.zip`, pour que `/releases/latest` reste stable.

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

L'URL par défaut est `https://fps-api.nathna.workers.dev`. Dans l'application : **Paramètres**, champ URL, **Enregistrer**, puis **Tester l'API**. Le test appelle `GET /api/health`. Cette adresse n'est pas synchronisée : chaque PC a la sienne.
