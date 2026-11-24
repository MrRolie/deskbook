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
7. **`TradeCsv.cs`** : lecture du fichier d'opérations, avec en-tête exact. *Pourquoi si strict :* toutes les
   erreurs sont listées avec leur numéro de ligne et rien n'est importé d'un fichier fautif ; un montant est lu
   avec `CultureInfo.InvariantCulture`, sinon une machine en français lirait « 1.5 » autrement.
8. **`Attribution.cs`** : la mention de la source et du caractère fictif, en un seul endroit.

## 2. Le monde extérieur : `src/Deskbook.Infrastructure/`

9. **`Valet/ValetParser.cs`** : lit la réponse JSON de la Banque. Les valeurs arrivent en chaînes ; une série
   absente ou vide est ignorée, jamais lue comme zéro ; les jours sont triés car l'ordre de l'API dépend de la
   requête.
10. **`Valet/ValetClient.cs`** : une requête par appel, sans boucle de reprise, et la réponse mise en cache par
    plage et par jour UTC. *Pourquoi :* les conditions de la Banque interdisent de contourner ses limites, et
    son guide demande de mettre en cache ce qui change une fois par jour. `TimeProvider` est injecté pour que les
    tests fixent « aujourd'hui ».
11. **`Postgres/schema.sql`** : quatre tables. `trade` (jamais modifiée), `rate_fetch` (chaque requête et le
    dernier jour qu'elle couvre), `rate` (valeur courante), `rate_revision` (valeurs remplacées). `numeric` sans
    précision fixe garde l'échelle publiée. Chaque instruction est `IF NOT EXISTS` : `db init` se relance.
12. **`Postgres/PostgresStore.cs`** : tout le SQL, écrit à la main avec des paramètres (`$1`, `$2`…), jamais de
    concaténation de valeurs. Les écritures à plusieurs lignes sont dans une transaction : un import en conflit ne
    laisse rien derrière lui. *Pourquoi pas d'ORM :* quatre tables et des requêtes courtes ; le SQL visible est plus
    simple à expliquer et à vérifier.

## 3. La ligne de commande : `src/Deskbook.Cli/`

13. **`Program.cs`** : une ligne ; tout est dans `App`.
14. **`App.cs`** : l'aiguillage des commandes et la traduction des exceptions en codes de sortie (0, 1, 2, 3).
    *Pourquoi `RunAsync(args, stdout, stderr, env)` :* les tests d'intégration appellent exactement ce que la
    console appelle, avec leur propre base, leur propre horloge et une fausse Banque.
15. **`CliArgs.cs`** : un petit analyseur ; une option inconnue est une erreur, pour qu'une faute de frappe ne
    soit jamais ignorée.
16. **`BookReport.cs`** et **`Output.cs`** : le livre en JSON (noms en `snake_case`, `"fictional": true`, la
    source) ou en tableau. L'arrondi au cent se fait ici, et seulement ici.

## 4. Les preuves : `tests/`

17. **`Deskbook.Tests/BookCalculatorTests.cs`** : les calculs faits à la main sont dans les commentaires, à côté
    des assertions. C'est le meilleur endroit pour expliquer le P&L.
18. **`Deskbook.IntegrationTests/TestDatabase.cs`** : chaque classe de tests a son propre schéma PostgreSQL, créé
    puis supprimé. Sans base, les tests échouent ; ils ne sont jamais ignorés.
19. **`Deskbook.IntegrationTests/CommandLineTests.cs`** : le CLI de bout en bout, sur un vrai PostgreSQL ; seule la
    Banque est remplacée, par une réponse réelle enregistrée (`tests/Fixtures/`).
20. **`scripts/check-tests-can-fail.sh`** : fausse une règle à la fois et exige que les tests échouent.

## 5. L'outillage : `scripts/dev.sh`, `docs/toolchain.md`, `docs/runbook.md`

Rien d'installé sur la machine : le SDK et PostgreSQL tournent dans leurs images officielles, épinglées par
empreinte, sous l'utilisateur courant pour que les fichiers produits lui appartiennent.

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
7. **`TradeCsv.cs`**: reads the trade file, with an exact header. *Why so strict:* every error is listed with its
   line number and nothing is imported from a faulty file; amounts are read with `CultureInfo.InvariantCulture`,
   or a French-locale machine would read "1.5" differently.
8. **`Attribution.cs`**: the source attribution and the fictional notice, in one place.

### 2. The outside world: `src/Deskbook.Infrastructure/`

9. **`Valet/ValetParser.cs`**: reads the Bank's JSON answer. Values arrive as strings; a missing or empty series is
   skipped, never read as zero; days are sorted because the API's order depends on the query.
10. **`Valet/ValetClient.cs`**: one request per call, no retry loop, and the answer cached by range and UTC day.
    *Why:* the Bank's terms forbid circumventing its limits, and its guide asks that once-a-day data be cached.
    `TimeProvider` is injected so tests can fix "today".
11. **`Postgres/schema.sql`**: four tables. `trade` (never updated), `rate_fetch` (each request and the last day it
    covers), `rate` (current value), `rate_revision` (replaced values). `numeric` without a fixed scale keeps the
    published precision. Every statement is `IF NOT EXISTS`: `db init` can be rerun.
12. **`Postgres/PostgresStore.cs`**: all the SQL, hand-written with parameters (`$1`, `$2`…), never values
    concatenated in. Multi-row writes run in one transaction: a conflicting import leaves nothing behind. *Why no
    ORM:* four tables and short queries; visible SQL is simpler to explain and to check.

### 3. The command line: `src/Deskbook.Cli/`

13. **`Program.cs`**: one line; everything is in `App`.
14. **`App.cs`**: dispatches commands and turns exceptions into exit codes (0, 1, 2, 3). *Why
    `RunAsync(args, stdout, stderr, env)`:* the integration tests call exactly what the console calls, with their
    own database, their own clock and a stand-in Bank.
15. **`CliArgs.cs`**: a small parser; an unknown option is an error, so a typo is never ignored.
16. **`BookReport.cs`** and **`Output.cs`**: the book as JSON (`snake_case` names, `"fictional": true`, the source)
    or as a table. Rounding to the cent happens here and only here.

### 4. The evidence: `tests/`

17. **`Deskbook.Tests/BookCalculatorTests.cs`**: the hand calculations sit in comments beside the assertions. The
    best place to explain the P&L from.
18. **`Deskbook.IntegrationTests/TestDatabase.cs`**: each test class gets its own PostgreSQL schema, created then
    dropped. With no database the tests fail; they never skip.
19. **`Deskbook.IntegrationTests/CommandLineTests.cs`**: the CLI end to end on a real PostgreSQL; only the Bank is
    replaced, by a saved real answer (`tests/Fixtures/`).
20. **`scripts/check-tests-can-fail.sh`**: breaks one rule at a time and requires the tests to fail.

### 5. Tooling: `scripts/dev.sh`, `docs/toolchain.md`, `docs/runbook.md`

Nothing installed on the machine: the SDK and PostgreSQL run in their official images, pinned by digest, as the
calling user so the files they produce belong to that user.
