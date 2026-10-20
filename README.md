# DatabaseSync

Outil CLI .NET 8 multi-plateforme de synchronisation MySQL **hors ligne**. Il exporte
les tables configurées dans un ZIP transportable, puis les importe exclusivement
avec `INSERT ... ON DUPLICATE KEY UPDATE`. Une ligne absente est insérée, une ligne
existante est mise à jour et les lignes présentes uniquement à destination restent
intactes.

## Garanties

- aucune instruction `DELETE`, `TRUNCATE` ou `REPLACE INTO` ;
- schéma et clés primaires détectés via `INFORMATION_SCHEMA` ;
- export JSON en streaming et import par lots, avec mémoire bornée ;
- contrôle SHA-256 de chaque fichier avant toute modification ;
- sauvegarde complète `mysqldump` avant l'ouverture de la transaction d'import ;
- transaction unique, `COMMIT` complet ou `ROLLBACK` complet ;
- noms de tables explicitement autorisés par la configuration ;
- même binaire source pour Windows Server 2022 et Linux.

## Prérequis

- SDK/runtime .NET 8 ;
- MySQL accessible localement depuis la machine qui exécute la commande ;
- `mysqldump` installé et disponible dans le `PATH` (ou chemin configuré) ;
- compte MySQL avec droits de lecture pour l'export et de lecture/écriture pour
  l'import. Les droits nécessaires à `mysqldump` dépendent des objets de la base.

## Configuration

Copier et modifier `src/DatabaseSync/appsettings.json`. Au déploiement, placer ce
fichier à côté de `dbsync`. `ConnectionString` désigne la base active.
`CompareConnectionString` désigne la seconde base pour `compare`.

Les secrets peuvent être fournis par variables d'environnement, par exemple :

```powershell
$env:DBSYNC_DatabaseSync__ConnectionString = "Server=localhost;Database=prod;User ID=dbsync;Password=secret"
```

```bash
export DBSYNC_DatabaseSync__ConnectionString='Server=localhost;Database=prod;User ID=dbsync;Password=secret'
```

Pour ajouter une table, ajouter uniquement son nom dans `Tables`. Chaque table doit
avoir une clé primaire. L'ordre des tables dans la configuration est l'ordre
d'import ; placez les tables parentes avant les tables enfants si des clés
étrangères existent.

## Utilisation

```text
dbsync export
dbsync export /media/usb/Sync_20260728.zip
dbsync import /media/usb/Sync_20260728.zip
dbsync compare
```

L'import refuse les tables non autorisées, les checksums invalides, les différences
de schéma et les packages dont le nombre réel de lignes diffère du manifeste.

## Compilation et publication

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet publish src/DatabaseSync -c Release -r win-x64 --self-contained false
dotnet publish src/DatabaseSync -c Release -r linux-x64 --self-contained false
```

## Format du package

```text
Sync_20260728.zip
├── manifest.json
├── Produit.json
├── Article.json
└── ...
```

Le manifeste versionné contient la date UTC, la liste des tables, leur schéma
(colonnes, types, ordre, clé primaire, auto-incrément), le nombre de lignes et le
SHA-256 du fichier JSON non compressé. Les valeurs binaires sont encodées en Base64.

## Limites opérationnelles

- Le package constitue un instantané logique table par table. Pour un instantané
  cohérent entre plusieurs tables très actives, exécuter l'export pendant une
  fenêtre stable ou avec un compte pointant vers une copie cohérente.
- `compare` nécessite que les deux bases soient accessibles simultanément depuis
  la même machine ; il ne modifie aucune donnée.
- La transaction protège les données importées, mais la sauvegarde `mysqldump`
  reste un fichier distinct et durable.
