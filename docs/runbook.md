# Installation et exploitation / Installing and running

*Français d'abord ; [English below](#english).*

## Installer

1. Docker est le seul prérequis. Les images sont épinglées par empreinte dans `scripts/dev.sh`
   (et consignées dans `docs/toolchain.md`).
2. `scripts/dev.sh db-up` démarre PostgreSQL sur un réseau Docker privé, sans port publié, avec un volume
   `deskbook-pgdata`. Le mot de passe est généré au premier lancement dans `.dev/pg.env` (ignoré par git).
3. `deskbook db init` crée les tables ; la commande peut être relancée sans effet.

Dans ce document, `deskbook` veut dire `scripts/dev.sh dotnet run --project src/Deskbook.Cli --`.

## Chaque jour

La Banque publie ses taux quotidiens vers 16 h 30 (heure de l'Est), avec parfois un court délai dans Valet.
Après 17 h :

```
deskbook rates fetch --from <lundi de la semaine précédente>
deskbook trades import <fichier du jour>.csv
deskbook book --json > book-<date>.json
```

- `--to` vaut par défaut la date du jour en UTC. Demander un jour pas encore publié est sans danger : une requête
  ne couvre que jusqu'à son dernier jour avec des taux, jamais au-delà.
- Une même plage demandée deux fois le même jour UTC est lue dans le cache (`data/cache/`), sans requête.
- La sortie JSON est stable : la comparer à celle de la veille est un contrôle valable.

## Quand une exécution échoue

| Code | Signe | Quoi faire |
|---|---|---|
| 3 | `refused: the Bank of Canada published no rates for <date>` | Jour férié, fin de semaine, ou taux pas encore publiés. Avant 17 h : réessayer plus tard. Sinon, c'est un jour sans livre. |
| 3 | `refused: rates for <date> were never fetched` ou `... between ... were never fetched` | Lancer `rates fetch` sur une plage qui couvre ces jours. |
| 3 | `refused: no <CCY> rate published for <date>` | La Banque ne publie pas cette devise ce jour-là. Pas de contournement : la position ne peut pas être évaluée. |
| 1 | `Valet answered 4xx/5xx` ou délai dépassé | Attendre au moins quelques minutes, puis réessayer **une** fois. Ne jamais boucler : les conditions de la Banque interdisent de contourner ses limites. Si l'échec persiste, le livre attend le lendemain. |
| 1 | erreur de connexion PostgreSQL | `scripts/dev.sh db-up`, puis relancer la commande. |
| 1 | `trade '<id>' is already stored with different fields` | Le fichier modifie une opération existante. Rien n'a été importé. Corriger le fichier : une opération ne se réécrit pas. |
| 1 | `line N: ...` à l'import | Chaque ligne fautive est listée ; rien n'a été importé. |
| 2 | erreur d'usage | Voir `deskbook help`. |

Une révision de taux (`n revised` à la récupération) n'est pas une erreur : l'ancienne valeur est conservée
(`deskbook rates revisions`) et les livres relancés utilisent la nouvelle.

## Une tâche de nuit

Rien n'est installé comme service. Pour une exécution automatique, une minuterie systemd ou une entrée cron lance
les trois commandes ci-dessus après 17 h, garde la sortie JSON, et alerte sur tout code non nul. Une version de
production construirait une image d'exécution plus légère que le SDK ; elle n'est pas faite ici.

---

## English

### Install

1. Docker is the only prerequisite. The images are pinned by digest in `scripts/dev.sh` (recorded in
   `docs/toolchain.md`).
2. `scripts/dev.sh db-up` starts PostgreSQL on a private Docker network, with no published port and a
   `deskbook-pgdata` volume. The password is generated on first start into `.dev/pg.env` (ignored by git).
3. `deskbook db init` creates the tables; running it again changes nothing.

In this document, `deskbook` means `scripts/dev.sh dotnet run --project src/Deskbook.Cli --`.

### Each day

The Bank publishes its daily rates around 16:30 Eastern time, sometimes with a short delay in Valet. After 17:00:

```
deskbook rates fetch --from <Monday of the previous week>
deskbook trades import <today's file>.csv
deskbook book --json > book-<date>.json
```

- `--to` defaults to today's UTC date. Asking for a day not yet published is safe: a fetch covers only up to its
  last day with rates, never beyond.
- The same range asked twice on the same UTC day is read from the cache (`data/cache/`), with no request.
- The JSON output is stable: comparing it with the previous day's is a valid check.

### When a run fails

| Code | Sign | What to do |
|---|---|---|
| 3 | `refused: the Bank of Canada published no rates for <date>` | Holiday, weekend, or rates not yet published. Before 17:00: try later. Otherwise it is a day with no book. |
| 3 | `refused: rates for <date> were never fetched` or `... between ... were never fetched` | Run `rates fetch` over a range covering those days. |
| 3 | `refused: no <CCY> rate published for <date>` | The Bank does not publish that currency that day. No workaround: the position cannot be valued. |
| 1 | `Valet answered 4xx/5xx` or a timeout | Wait at least a few minutes, then retry **once**. Never loop: the Bank's terms forbid circumventing its limits. If it keeps failing, the book waits for the next day. |
| 1 | PostgreSQL connection error | `scripts/dev.sh db-up`, then rerun the command. |
| 1 | `trade '<id>' is already stored with different fields` | The file changes an existing trade. Nothing was imported. Fix the file: a trade is not rewritten. |
| 1 | `line N: ...` on import | Every bad line is listed; nothing was imported. |
| 2 | usage error | See `deskbook help`. |

A rate revision (`n revised` on a fetch) is not an error: the old value is kept (`deskbook rates revisions`) and
rerun books use the new one.

### A nightly job

Nothing is installed as a service. For automatic runs, a systemd timer or cron entry runs the three commands above
after 17:00, keeps the JSON output, and alerts on any non-zero code. A production version would build a runtime
image lighter than the SDK; that is not done here.
