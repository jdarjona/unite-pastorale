# Unidad Pastoral

Site web pour une unité pastorale composée de cinq paroisses : photos, actualités, événements, documents et calendrier par paroisse.

## Architecture

- **React + Vite** (`src/web`) : site public et panneau `/admin`.
- **ASP.NET Core 9** (`src/UnidadPastoral.Api`) : API REST, authentification JWT et fichiers envoyés.
- **SQLite** : base de données `unidad-pastoral.db` créée au démarrage de l’API.

Les paroisses d’exemple sont chargées dans `src/UnidadPastoral.Api/Data/DbSeeder.cs`. C’est l’emplacement idéal pour les premières modifications : noms réels, adresses et horaires des messes.

## Exécution du projet

Vous devez disposer du [.NET 9 SDK](https://dotnet.microsoft.com/download) et de [Node.js LTS](https://nodejs.org/).

Terminal 1 — API :

```powershell
cd src/UnidadPastoral.Api
dotnet run --launch-profile http
```

L’API est accessible à l’adresse `http://localhost:5214`.

Terminal 2 — site web :

```powershell
cd src/web
npm install
npm run dev
```

Ouvrez `http://localhost:5173`.

Panneau de publication : `/admin`  
Utilisateur initial : `admin` / `CambiaEsto1`

Vite redirige `/api` et `/uploads` vers l’API, afin que les photos et les documents soient visibles depuis la même origine.

## Publier une démonstration gratuitement

Le site peut être hébergé sur Vercel et l’API sur Render. Il n’est pas nécessaire d’acheter un domaine : les deux plateformes fournissent une URL publique.

### Base de données sur Neon

1. Créez un projet PostgreSQL dans la [console Neon](https://console.neon.tech/). La base de données et son rôle sont créés avec le projet.
2. Dans le tableau de bord du projet, ouvrez **Connect** et récupérez les deux chaînes de connexion :
   - La chaîne **pooled** (hôte contenant `-pooler`) pour `DATABASE_URL`, utilisée par l’API pour ses requêtes.
   - La chaîne **directe** (hôte sans `-pooler`) pour `DATABASE_URL_UNPOOLED`, utilisée pour les migrations.
3. Dans Render, ajoutez ces deux valeurs comme variables d’environnement et définissez `Database__Provider` sur `PostgreSql`. Ne publiez pas les chaînes de connexion dans le dépôt : elles contiennent les identifiants de la base.

Au démarrage, l’API applique automatiquement les migrations PostgreSQL en attente avec la connexion directe, puis crée les données initiales (paroisses et administrateur) si nécessaire. Aucune commande SQL manuelle n’est requise pour initialiser le schéma.

### Photos et documents envoyés

En production, les photos et documents envoyés depuis le panneau d’administration sont stockés dans **Neon Object Storage**, dans un bucket nommé `assets`. Ce stockage doit être configuré sur la même branche Neon que la base de données :

1. Dans la console Neon, ouvrez le projet et la branche utilisés par l’API, puis créez le bucket `assets`.
2. Créez des identifiants de stockage avec les autorisations de lecture et d’écriture. Récupérez le point de terminaison S3 et la région de cette branche.
3. Dans les variables d’environnement Render, renseignez `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_ENDPOINT_URL_S3` et `AWS_REGION` avec ces valeurs. Elles figurent dans le tableau de la section suivante. Gardez ces identifiants secrets et ne les ajoutez pas au dépôt.
4. Configurez le stockage avant le premier déploiement ou avant l’importation initiale avec `Demo__ImportSnapshot=true` : les fichiers de l’instantané sont alors copiés dans le bucket `assets`.

L’API enregistre les fichiers dans le bucket et les sert via les chemins `/uploads/...` ; le navigateur n’accède pas directement au bucket. En développement local, si les variables S3 ne sont pas définies, les fichiers sont enregistrés dans le dossier local `uploads/`. Ne vous fiez pas à ce stockage local pour les fichiers de production.

### API sur Render

1. Ouvrez le [tableau de bord Render](https://dashboard.render.com/) et créez un **Web Service** connecté au dépôt `jdarjona/unidad-pastoral`.
2. Sélectionnez **Docker** comme environnement et choisissez le plan **Free**. Le `Dockerfile` se trouve à la racine du dépôt.
3. Avant le déploiement, ajoutez les variables suivantes dans **Environment** :

| Variable | Valeur |
| --- | --- |
| `Jwt__Key` | Secret aléatoire d’au moins 32 caractères |
| `Admin__Username` | Nom d’utilisateur de l’administrateur |
| `Admin__InitialPassword` | Mot de passe initial unique et sécurisé |
| `Cors__AllowedOrigins` | URL Vercel, par exemple `https://unidad-pastoral.vercel.app` |
| `Database__Provider` | `PostgreSql` pour la production avec Neon ; `Sqlite` est conservé en local |
| `DATABASE_URL` | Chaîne de connexion Neon avec pool pour les requêtes de l’API |
| `DATABASE_URL_UNPOOLED` | Chaîne de connexion directe Neon pour appliquer les migrations |
| `AWS_ACCESS_KEY_ID` | Identifiant de stockage d’objets généré par Neon |
| `AWS_SECRET_ACCESS_KEY` | Secret de stockage d’objets généré par Neon |
| `AWS_ENDPOINT_URL_S3` | Point d’accès S3 généré par Neon |
| `AWS_REGION` | Région du projet Neon, par exemple `eu-central-1` |
| `Demo__ImportSnapshot` | Définissez `true` pour la première importation, puis `false` après avoir vérifié les données |

4. Lancez le déploiement, puis copiez l’URL attribuée par Render à l’API, par exemple `https://unidad-pastoral-api.onrender.com`.

Au démarrage, l’API applique `InitialPostgres`. Avec `Demo__ImportSnapshot=true`, si la base PostgreSQL est vide, elle importe les paroisses, publications, événements, albums, photos et activités pastorales, puis copie les fichiers dans le bucket `assets`. Les utilisateurs et les hachages ne sont pas importés : l’administrateur est créé à partir de `Admin__Username` et `Admin__InitialPassword`. Vérifiez le site, puis désactivez l’option.

### Site web sur Vercel

1. Ouvrez [Vercel](https://vercel.com/new) et importez le même dépôt.
2. Configurez **Root Directory** sur `src/web`. La commande de compilation est `npm run build` et le dossier de sortie est `dist`.
3. Ajoutez cette variable d’environnement :

| Variable | Valeur |
| --- | --- |
| `VITE_API_URL` | URL de l’API sur Render, sans barre oblique finale |

4. Lancez le déploiement. Si Vercel attribue une URL différente de celle indiquée dans `Cors__AllowedOrigins`, mettez cette variable à jour dans Render puis redémarrez l’API.

### Limites de la démonstration gratuite

- Render peut suspendre l’API après 15 minutes sans visite ; la première requête suivante peut prendre environ une minute.
- Avec `Demo__RestoreOnStartup=true`, chaque démarrage restaure `src/UnidadPastoral.Api/demo-data/unidad-pastoral.db` ainsi que ses fichiers envoyés. L’instantané contient le contenu local empaqueté, mais aucun utilisateur ni aucun hachage de mot de passe : l’administrateur est créé à partir de `Admin__Username` et `Admin__InitialPassword`.
- Les modifications effectuées depuis le panneau de démonstration ne mettent pas à jour l’instantané ; au redémarrage, le contenu empaqueté est rétabli. Pour modifier la base initiale, mettez à jour les fichiers de `demo-data`, créez un commit et redéployez.
- Le plan Hobby de Vercel est destiné aux projets personnels et non commerciaux. Consultez ses conditions pour un site institutionnel ; vous pouvez aussi héberger le site statique sur Render.

## GitHub

Dépôt GitHub : [jdarjona/unidad-pastoral](https://github.com/jdarjona/unidad-pastoral).

La configuration distante `origin` pointe déjà vers ce dépôt. Pour envoyer les modifications ultérieures vers la branche `master` :

```powershell
cd C:\Users\jdarj\source\repos\unidad-pastoral
git add .
git commit -m "Description de la modification"
git push origin master
```
