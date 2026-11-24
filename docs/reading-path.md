# Parcours de lecture / Reading path

*Français d'abord ; [English below](#english).*

L'ordre pour lire le code, et pour chaque arrêt : ce qu'il fait, pourquoi il a cette forme. Environ une heure.

## 0. Les règles : `docs/problem.md`

Ce que le prototype a appris et les décisions de la première version. Tout le reste en découle : les deux jambes
comptent, le résultat se décompose, on refuse plutôt que deviner.

## 1. Le domaine : `src/Deskbook.Core/`

Aucune base de données, aucun réseau, aucune horloge : c'est la partie qui se teste le plus facilement, donc celle
qui porte les règles.

1. **`Currency.cs`** : un code ISO à trois lettres, en `readonly record struct`. *Pourquoi :* un type plutôt
   qu'une `string` empêche de passer « usd » ou un nom de contrepartie là où une devise est attendue ; le
   constructeur valide une fois, ensuite chaque `Currency` est correcte. Un `record` donne l'égalité par valeur,
   donc une `Currency` sert de clé de dictionnaire.
2. **`Trade.cs`** : une opération au comptant, deux `Leg` (achetée, vendue), montants positifs. `SignedLegs()`
   rend + achat, − vente. *Pourquoi :* les montants restent positifs à l'entrée (comme sur un ticket), le signe
   n'apparaît qu'au calcul. Le constructeur refuse une opération incohérente : un objet invalide ne peut pas exister.
3. **`RateSet.cs`** : les taux d'un jour, en dollars canadiens par unité. Le CAD vaut 1 et n'est jamais stocké.
   *Pourquoi :* `TryGetCadPerUnit` oblige l'appelant à traiter le cas « pas de taux » ; il n'y a pas de valeur
   par défaut qui vaudrait zéro.
4. **`BookCalculator.cs`** et **`Book.cs`** : le cœur. À partir des taux du jour, de ceux du jour publié
   précédent et des opérations :
   - positions d'ouverture = opérations jusqu'au jour précédent ; opérations nouvelles = celles d'après ;
   - réévaluation = position d'ouverture × (taux du jour − taux précédent) ;
   - résultat des opérations nouvelles = leurs jambes × taux du jour ;
   - valeur du livre = positions de clôture × taux du jour.
   La différence des valeurs est exactement la somme des deux parts (algèbre : clôture = ouverture + nouvelles) ;
   un test le vérifie. *Pourquoi statique et pur :* mêmes entrées, même livre, ce qui rend la reproductibilité
   vraie par construction. `decimal` partout : `0.1 + 0.2` vaut `0.3`, ce qui n'est pas vrai en `double`.
5. **`RateCalendar.cs`** et **`BookRefusedException.cs`** : quels jours ont des taux, quels jours ont été demandés.
   *Pourquoi les deux :* un jour demandé sans taux est un jour férié ; un jour jamais demandé est inconnu. Sans
   cette distinction, un trou dans les données ferait passer silencieusement un résultat de deux jours pour un
   seul. Le refus est une exception portant la liste des raisons, attrapée en un seul endroit (le CLI).
6. **`IBookSource.cs`** et **`BookService.cs`** : le service lit le calendrier, les deux jours de taux et les
   opérations, puis appelle le calculateur. *Pourquoi une interface :* le domaine définit ce dont il a besoin, et
   PostgreSQL l'implémente (inversion des dépendances). Le domaine ne référence pas Npgsql.
7. **`StrictCsv.cs`** et **`TradeCsv.cs`** : le seul dialecte CSV lu ici, puis le fichier d'opérations. *Pourquoi
   si strict :* toutes les erreurs sont listées avec leur numéro de ligne et rien n'est importé d'un fichier
   fautif ; un montant est lu avec `CultureInfo.InvariantCulture`, sinon une machine en français lirait « 1.5 »
   autrement. `StrictCsv.Read<T>` est générique : chaque fichier ne fournit que sa fonction de lecture d'une ligne.
7a. **`Reconciliation/`** : `Confirmation` (ce que la contrepartie confirme), `Break` (un écart et son genre),
   `ConfirmationCsv`, et **`Reconciler`**, pur comme le calculateur. Il apparie par l'identifiant d'opération
   cité, compare chaque champ sans tolérance (une confirmation est un document juridique), compare les montants
   comme des nombres (1000 = 1000.00), et pour une référence inconnue nomme les opérations non confirmées de même
   économie. Les écarts sortent toujours dans le même ordre.
8. **`Attribution.cs`** : la mention de la source et du caractère fictif, en un seul endroit.
8b. **`Translation.cs`** : un livre en CAD montré dans une autre devise. Valeur = valeur CAD / taux du jour ;
   chaque part du résultat est convertie au taux du jour, et l'effet de conversion = valeur CAD de la veille ×
   (1 / taux du jour − 1 / taux de la veille). *Pourquoi cette part :* sans elle, les parts converties ne font
   plus le total dès que le taux bouge ; avec elle, l'égalité est exacte et un test la vérifie.
8a. **`Bonds/`** : le livre d'obligations. `Bond` (coupon semestriel, périodes comptées à rebours depuis
   l'échéance pour garder les fins de mois), `YieldCurve` (rendements 2 à 10 ans, interpolation linéaire en
   années, refus hors courbe), `BondPricer` (prix = somme des flux actualisés à (1 + y/2)^(k − 1 + w), w = part de
   la période qui reste ; coupon couru actuel/actuel ; DV01 par réévaluation à ±1 pb), `BondBookCalculator` (valeur
   = obligations au prix plein + trésorerie des opérations ; résultat = détenu + nouvelles opérations).
   *Pourquoi `double` ici :* `decimal` n'a pas de puissance fractionnaire ; chaque valeur actualisée est arrondie à
   10 décimales avant d'entrer dans une somme `decimal`. *Pourquoi la trésorerie dans la valeur :* acheter au juste
   prix ne doit rien changer, et un coupon reçu compense la baisse du prix plein le jour du détachement.

## 2. Le monde extérieur : `src/Deskbook.Infrastructure/`

9. **`Valet/ValetParser.cs`** : lit la réponse JSON de la Banque. Les valeurs arrivent en chaînes ; une série
   absente ou vide est ignorée, jamais lue comme zéro ; les jours sont triés car l'ordre de l'API dépend de la
   requête.
10. **`Valet/ValetClient.cs`** : une requête par appel, sans boucle de reprise. La réponse est mise en cache par
    plage, seulement après avoir été lue sans erreur ; elle est réutilisée si elle atteint le dernier jour demandé
    ou date de moins de 15 minutes. *Pourquoi :* les conditions de la Banque interdisent de contourner ses limites
    et son guide demande de mettre en cache ce qui change une fois par jour ; mais une réponse obtenue avant la
    publication de 16 h 30 ne doit pas cacher les taux du jour jusqu'au lendemain. `TimeProvider` est injecté pour
    que les tests fixent l'heure et la fassent avancer.
11. **`Postgres/schema.sql`** : les tables du livre. `trade` (jamais modifiée), `rate_fetch` (chaque requête et le
    dernier jour qu'elle couvre), `rate` (valeur courante), `rate_revision` (valeurs remplacées). `numeric` sans
    précision fixe garde l'échelle publiée. Chaque instruction est `IF NOT EXISTS` ou `OR REPLACE` : `db init` se relance.
12. **`Postgres/PostgresStore.cs`** : tout le SQL, écrit à la main avec des paramètres (`$1`, `$2`…), jamais de
    concaténation de valeurs. Les écritures à plusieurs lignes sont dans une transaction : un import en conflit ne
    laisse rien derrière lui. *Pourquoi pas d'ORM :* sept tables et des requêtes courtes ; le SQL visible est plus
    simple à expliquer et à vérifier.
12b. **`Postgres/BondStore.cs`** : obligations, opérations sur obligations (ajout seulement, comme les autres) et
    rendements, avec leur propre calendrier (`rate_fetch` filtré par groupe de séries, pour qu'une requête de
    rendements ne fasse pas croire que des taux de change ont été demandés).
12a. **`Postgres/ReconStore.cs`** et la fin de `schema.sql` : la piste d'audit du rapprochement (exécutions avec
    l'empreinte SHA-256 du fichier, écarts, notes de résolution). Un déclencheur (`deskbook_append_only`) refuse
    UPDATE et DELETE sur ces tables et sur `trade` : la règle « on n'efface pas » est tenue par la base, pas
    seulement par le code.

## 3. La ligne de commande : `src/Deskbook.Cli/`

13. **`Program.cs`** : une ligne ; tout est dans `App`.
14. **`App.cs`** : l'aiguillage des commandes et la traduction des exceptions en codes de sortie (0, 1, 2, 3, 4) ;
    toute exception imprévue devient le code 1, dans le même format d'erreur, jamais une trace de pile. La classe
    interne `Services` ouvre un seul pool de connexions par exécution et le ferme à la fin : un appelant qui lance
    beaucoup de commandes dans un même processus (les tests) ne laisse fuir aucune connexion.
    *Pourquoi `RunAsync(args, stdout, stderr, env)` :* les tests d'intégration appellent exactement ce que la
    console appelle, avec leur propre base, leur propre horloge et une fausse Banque.
15. **`CliArgs.cs`** : un petit analyseur ; une option inconnue est une erreur, pour qu'une faute de frappe ne
    soit jamais ignorée.
16. **`src/Deskbook.Core/Reporting/BookReport.cs`** et **`Output.cs`** : le livre en JSON (noms en
    `snake_case`, `"fictional": true`, la source) ou en tableau. L'arrondi au cent se fait ici, et seulement ici.
    *Pourquoi dans Core :* la ligne de commande et la page s'en servent toutes deux ; c'est du formatage pur, sans
    entrée ni sortie.

## 3 bis. La page : `src/Deskbook.Web/`

16a. **`WebApp.cs`** : ASP.NET Core en « minimal API », deux routes en lecture seule : `/` (la page) et
    `/api/book` (le même JSON que le CLI ; un refus donne HTTP 409, une date illisible 400). `Build(args,
    connexion)` est séparé de `Program.cs` pour que les tests démarrent la vraie application sur un port libre.
16b. **`BookPage.cs`** : la page construite en chaîne, sans moteur de gabarit ni script ni fichier externe ; chaque
    valeur passe par `WebUtility.HtmlEncode`. Un gain et une perte se distinguent par leur signe écrit, pas
    seulement par la couleur. Un jour refusé affiche ses raisons, jamais un livre vide.

## 4. Les preuves : `tests/`

17. **`Deskbook.Tests/BookCalculatorTests.cs`** : les calculs faits à la main sont dans les commentaires, à côté
    des assertions. C'est le meilleur endroit pour expliquer le P&L.
18. **`Deskbook.IntegrationTests/TestDatabase.cs`** : chaque classe de tests a son propre schéma PostgreSQL, créé
    puis supprimé. Sans base, les tests échouent ; ils ne sont jamais ignorés.
19. **`Deskbook.IntegrationTests/CommandLineTests.cs`**, **`ValetCacheTests.cs`** et **`WebPageTests.cs`** : le
    CLI de bout en bout, le délai du cache avec une horloge qu'on avance, et la page servie sur un vrai port, tous
    sur un vrai PostgreSQL ; seule la Banque est remplacée, par une réponse réelle enregistrée (`tests/Fixtures/`).
20. **`scripts/check-tests-can-fail.sh`** : fausse une règle à la fois et exige que les tests échouent ; il exige
    aussi que les tests passent sans rien de faussé, avant et après, pour qu'une base arrêtée ne passe pas pour
    une détection.

## 5. L'outillage : `scripts/dev.sh`, `docs/toolchain.md`, `docs/runbook.md`

Rien d'installé sur la machine : le SDK et PostgreSQL tournent dans leurs images officielles, épinglées par
empreinte, sous l'utilisateur courant pour que les fichiers produits lui appartiennent.

## 6. Le déploiement : `Dockerfile`, `deploy/`

- **`Dockerfile`** : deux étapes. Le SDK compile et publie le CLI et la page ; l'image d'exécution ASP.NET, sans
  compilateur, les fait tourner sous l'utilisateur non privilégié `app`. *Pourquoi deux étapes :* l'image livrée
  ne contient que ce qui tourne. `.dockerignore` tient le reste hors de la construction.
- **`deploy/compose.yaml`** : PostgreSQL sans port publié, la page sur une adresse donnée, la tâche du soir. Les
  secrets viennent de `deploy/.env`, jamais versionné.
- **`App.NightlyAsync`** (dans `App.cs`) : la soirée en une commande. Chaque étape est dans son propre `try` pour
  qu'une panne de Valet n'empêche pas d'écrire le livre avec les taux déjà stockés ; le code de sortie donne la
  priorité à la panne (1), puis au refus (3), puis aux écarts (4).
- **`deploy/systemd/`** : la minuterie, le service, et le service d'alerte lancé par `OnFailure=`.

---

## English

The order to read the code in, and for each stop what it does and why it is shaped that way. About an hour.

### 0. The rules: `docs/problem.md`

What the spike taught and the first version's decisions. Everything else follows: both legs count, P&L splits,
refuse rather than guess.

### 1. The domain: `src/Deskbook.Core/`

No database, no network, no clock: the easiest part to test, so the part that carries the rules.

1. **`Currency.cs`**: a three-letter ISO code as a `readonly record struct`. *Why:* a type instead of a `string`
   stops "usd" or a counterparty name being passed where a currency is expected; the constructor validates once,
   and every `Currency` after that is correct. A `record` gives value equality, so a `Currency` works as a
   dictionary key.
2. **`Trade.cs`**: a spot trade with two `Leg`s (bought, sold), positive amounts. `SignedLegs()` returns + bought,
   − sold. *Why:* amounts stay positive on entry (as on a ticket); the sign only appears in the calculation. The
   constructor rejects an inconsistent trade: an invalid object cannot exist.
3. **`RateSet.cs`**: one day's rates, in Canadian dollars per unit. CAD is 1 and never stored. *Why:*
   `TryGetCadPerUnit` makes the caller handle "no rate"; there is no default that would silently be zero.
4. **`BookCalculator.cs`** and **`Book.cs`**: the core. From the day's rates, the previous published day's, and
   the trades:
   - opening positions = trades up to the previous day; new trades = those after;
   - revaluation = opening position × (today's rate − previous rate);
   - new-trade P&L = their legs × today's rate;
   - book value = closing positions × today's rate.
   The change in value is exactly the sum of the two parts (algebra: closing = opening + new); a test checks it.
   *Why static and pure:* same inputs, same book, so reproducibility holds by construction. `decimal` throughout:
   `0.1 + 0.2` is `0.3`, which is not true in `double`.
5. **`RateCalendar.cs`** and **`BookRefusedException.cs`**: which days have rates and which days were asked for.
   *Why both:* a day asked for with no rates is a holiday; a day never asked for is unknown. Without the
   distinction, a hole in the data would silently pass a two-day P&L off as one day's. A refusal is an exception
   carrying its list of reasons, caught in one place (the CLI).
6. **`IBookSource.cs`** and **`BookService.cs`**: the service reads the calendar, two days of rates and the
   trades, then calls the calculator. *Why an interface:* the domain states what it needs and PostgreSQL
   implements it (dependency inversion). The domain does not reference Npgsql.
7. **`StrictCsv.cs`** and **`TradeCsv.cs`**: the one CSV dialect read here, then the trade file. *Why so strict:*
   every error is listed with its line number and nothing is imported from a faulty file; amounts are read with
   `CultureInfo.InvariantCulture`, or a French-locale machine would read "1.5" differently. `StrictCsv.Read<T>` is
   generic: each file supplies only its one-line reader.
7a. **`Reconciliation/`**: `Confirmation` (what the counterparty confirms), `Break` (a break and its kind),
   `ConfirmationCsv`, and **`Reconciler`**, pure like the calculator. It pairs by the quoted trade id, compares
   every field with no tolerance (a confirmation is a legal record), compares amounts as numbers (1000 = 1000.00),
   and for an unknown reference names the unconfirmed trades with the same economics. Breaks always come out in
   the same order.
8. **`Attribution.cs`**: the source attribution and the fictional notice, in one place.
8b. **`Translation.cs`**: a CAD book shown in another currency. Value = CAD value / today's rate; each P&L part is
   converted at today's rate, and the translation effect = previous CAD value × (1 / today's rate − 1 / previous
   rate). *Why that part:* without it the converted parts stop adding up to the total as soon as the rate moves;
   with it the equality is exact, and a test checks it.
8a. **`Bonds/`**: the bond book. `Bond` (semi-annual coupon, periods counted back from maturity to keep month
   ends), `YieldCurve` (2 to 10-year yields, linear interpolation in years, refusal outside the curve), `BondPricer`
   (price = sum of cash flows discounted at (1 + y/2)^(k − 1 + w), w = share of the period left; accrued
   actual/actual; DV01 by repricing at ±1 bp), `BondBookCalculator` (value = bonds at full price + the trades'
   cash; P&L = held + new trades). *Why `double` here:* `decimal` has no fractional power; each present value is
   rounded to 10 decimals before it enters a `decimal` sum. *Why cash is in the value:* buying at a fair price must
   change nothing, and a coupon received offsets the full price's drop on the coupon date.

### 2. The outside world: `src/Deskbook.Infrastructure/`

9. **`Valet/ValetParser.cs`**: reads the Bank's JSON answer. Values arrive as strings; a missing or empty series is
   skipped, never read as zero; days are sorted because the API's order depends on the query.
10. **`Valet/ValetClient.cs`**: one request per call, no retry loop. The answer is cached by range, only after it
    parses; it is reused if it reaches the last day asked for or is less than 15 minutes old. *Why:* the Bank's
    terms forbid circumventing its limits and its guide asks that once-a-day data be cached; but an answer fetched
    before the 16:30 publication must not hide the day's rates until the next day. `TimeProvider` is injected so
    tests can fix the time and move it forward.
11. **`Postgres/schema.sql`**: the book's tables. `trade` (never updated), `rate_fetch` (each request and the last day it
    covers), `rate` (current value), `rate_revision` (replaced values). `numeric` without a fixed scale keeps the
    published precision. Every statement is `IF NOT EXISTS` or `OR REPLACE`: `db init` can be rerun.
12. **`Postgres/PostgresStore.cs`**: all the SQL, hand-written with parameters (`$1`, `$2`…), never values
    concatenated in. Multi-row writes run in one transaction: a conflicting import leaves nothing behind. *Why no
    ORM:* seven tables and short queries; visible SQL is simpler to explain and to check.
12b. **`Postgres/BondStore.cs`**: bonds, bond trades (insert-only, like the others) and yields, with their own
    calendar (`rate_fetch` filtered by series group, so a yields fetch never makes exchange rates look fetched).
12a. **`Postgres/ReconStore.cs`** and the end of `schema.sql`: the reconciliation audit trail (runs with the file's
    SHA-256, breaks, resolution notes). A trigger (`deskbook_append_only`) refuses UPDATE and DELETE on those
    tables and on `trade`: the "nothing is erased" rule is held by the database, not only by the code.

### 3. The command line: `src/Deskbook.Cli/`

13. **`Program.cs`**: one line; everything is in `App`.
14. **`App.cs`**: dispatches commands and turns exceptions into exit codes (0, 1, 2, 3, 4); any unexpected exception
    becomes code 1, in the same error shape, never a stack trace. The inner `Services` class opens one connection
    pool per run and closes it at the end: a caller running many commands in one process (the tests) leaks no
    connection. *Why
    `RunAsync(args, stdout, stderr, env)`:* the integration tests call exactly what the console calls, with their
    own database, their own clock and a stand-in Bank.
15. **`CliArgs.cs`**: a small parser; an unknown option is an error, so a typo is never ignored.
16. **`src/Deskbook.Core/Reporting/BookReport.cs`** and **`Output.cs`**: the book as JSON (`snake_case` names,
    `"fictional": true`, the source) or as a table. Rounding to the cent happens here and only here. *Why in Core:*
    both the command line and the page use them; it is pure formatting, no input or output.

### 3b. The page: `src/Deskbook.Web/`

16a. **`WebApp.cs`**: ASP.NET Core minimal API, two read-only routes: `/` (the page) and `/api/book` (the same JSON
    as the CLI; a refusal is HTTP 409, an unreadable date 400). `Build(args, connection)` is separate from
    `Program.cs` so the tests start the real application on a free port.
16b. **`BookPage.cs`**: the page built as a string, with no template engine, script or external file; every value
    goes through `WebUtility.HtmlEncode`. A gain and a loss differ by their written sign, not only by colour. A
    refused day shows its reasons, never an empty book.

### 4. The evidence: `tests/`

17. **`Deskbook.Tests/BookCalculatorTests.cs`**: the hand calculations sit in comments beside the assertions. The
    best place to explain the P&L from.
18. **`Deskbook.IntegrationTests/TestDatabase.cs`**: each test class gets its own PostgreSQL schema, created then
    dropped. With no database the tests fail; they never skip.
19. **`Deskbook.IntegrationTests/CommandLineTests.cs`**, **`ValetCacheTests.cs`** and **`WebPageTests.cs`**: the
    CLI end to end, the cache's timing with a clock the test moves, and the page served on a real port, all on a
    real PostgreSQL; only the Bank is replaced, by a saved real answer (`tests/Fixtures/`).
20. **`scripts/check-tests-can-fail.sh`**: breaks one rule at a time and requires the tests to fail; it also
    requires them to pass with nothing broken, before and after, so a stopped database never counts as a catch.

### 5. Tooling: `scripts/dev.sh`, `docs/toolchain.md`, `docs/runbook.md`

Nothing installed on the machine: the SDK and PostgreSQL run in their official images, pinned by digest, as the
calling user so the files they produce belong to that user.

### 6. Deployment: `Dockerfile`, `deploy/`

- **`Dockerfile`**: two stages. The SDK compiles and publishes the CLI and the page; the ASP.NET runtime image, with
  no compiler, runs them as the unprivileged `app` user. *Why two stages:* the shipped image holds only what runs.
  `.dockerignore` keeps everything else out of the build.
- **`deploy/compose.yaml`**: PostgreSQL with no published port, the page on a given address, the evening job.
  Secrets come from `deploy/.env`, never committed.
- **`App.NightlyAsync`** (in `App.cs`): the evening in one command. Each step has its own `try` so a Valet outage
  does not stop the book being written from the rates already stored; the exit code ranks a failure (1) above a
  refusal (3) above breaks (4).
- **`deploy/systemd/`**: the timer, the service, and the alert service started by `OnFailure=`.
