# deskbook

*Français d'abord ; [English below](#english).*

Le livre d'un pupitre de change **fictif** : des opérations au comptant entrent, des positions par devise et un
résultat quotidien (P&L) en dollars canadiens sortent, évalués aux taux de change réels publiés par la Banque du
Canada. C# sur .NET 10, PostgreSQL 18, une ligne de commande avec sortie JSON.

Le pupitre, ses contreparties, ses obligations, ses opérations et ses positions sont inventés. Seules les données
de marché sont réelles : taux de change et rendements obligataires de référence de la Banque du Canada.

## Ce qu'il fait

```
$ deskbook rates fetch --from 2026-11-02 --to 2026-11-23
15 day(s) with rates, covered to 2026-11-23: 360 added, 0 unchanged, 0 revised.
$ deskbook trades import samples/trades-fictional.csv
8 trade(s) added, 0 already present.
$ deskbook book --date 2026-11-23
Demo Rates & FX Desk (fictional): book for 2026-11-23 (previous published day: 2026-11-20)

Ccy            Position  CAD per unit       Value CAD    Reval P&L  New trades P&L
CAD         -474,400.00             1     -474,400.00         0.00     -574,500.00
CHF          300,000.00        1.7063      511,890.00      -120.00            0.00
EUR         -824,000.00        1.5672   -1,291,372.80     4,532.00            0.00
JPY      100,000,000.00      0.009030      903,000.00         0.00            0.00
USD          274,000.00        1.4369      393,710.60      -554.40      574,760.00

Book value: CAD 42,827.80 (previous CAD 38,710.20)
Day's P&L:  CAD 4,117.60 = new trades 260.00 + revaluation 3,857.60
New trades: FICT-0008
```

- **Le résultat du jour** = valeur du livre − valeur au jour publié précédent, décomposé en résultat des opérations
  nouvelles et réévaluation des positions de la veille ; les tests vérifient que les deux parts font le total.
- **Refuser plutôt que deviner :** pas de livre pour un jour sans taux publié (le 2026-11-11, jour férié), pour une
  devise sans taux, ni quand les taux stockés laissent un trou. Code de sortie 3 et la raison.
- **Reproductible :** relancer le livre donne la même sortie, octet pour octet.
- **Taux révisés :** une valeur modifiée par la Banque est mise à jour et l'ancienne conservée.
- **Livre d'obligations :** des obligations fictives de type fédéral, évaluées chaque jour à partir des rendements
  de référence de la Banque du Canada (2 à 10 ans, interpolés), avec coupon couru, prix pied de coupon, DV01 et
  résultat du jour (positions détenues + opérations nouvelles). C'est un prix de modèle, dit comme tel ; une
  échéance hors de la courbe est refusée, jamais extrapolée. `deskbook yields fetch`, `deskbook bonds book`.
- **Une seconde devise de présentation :** `--currency USD` (ou toute devise publiée) ajoute au livre, en CAD,
  sa valeur et son résultat dans cette devise, au taux de chaque jour. Le résultat y gagne une part : l'effet de
  conversion (la valeur de la veille réévaluée à la variation du taux) ; les parts font toujours le total.
- **Rapprochement :** `deskbook recon run <confirmations.csv>` compare les opérations du pupitre aux confirmations
  (fictives) des contreparties et liste les écarts : confirmation manquante, opération inconnue, doublon, champ
  différent avec les deux valeurs. Chaque exécution est gardée avec l'empreinte SHA-256 du fichier lu ; un écart
  reçoit des notes de résolution. Ces tables, comme celle des opérations, n'acceptent que des ajouts : la base
  refuse toute modification ou suppression.
- **Déploiement et tâche du soir :** une image Docker (SDK pour construire, image d'exécution ASP.NET pour tourner),
  `deploy/compose.yaml`, et une minuterie systemd qui lance `deskbook nightly` chaque soir de semaine et alerte sur
  tout échec. Voir [`docs/runbook.md`](docs/runbook.md).
- `--json` sur chaque commande, pour un script ou un agent. Codes de sortie : 0 fait, 1 panne, 2 usage, 3 refus,
  4 rapprochement terminé avec des écarts.
- **Des pages web** en lecture seule : le livre de change, le livre d'obligations avec la courbe de référence, et
  les écarts du dernier rapprochement. Un ruban montre le résultat de chaque jour publié ; chaque barre mène à ce
  jour. Le choix de devise montre le livre dans une seconde devise. JSON à `/api/book` et `/api/bonds`, identique
  au CLI. Un jour refusé s'affiche comme un refus.

Les règles et leur raison : [`docs/problem.md`](docs/problem.md). L'ordre de lecture du code :
[`docs/reading-path.md`](docs/reading-path.md). L'installation et l'exploitation : [`docs/runbook.md`](docs/runbook.md).

## Lancer

Seul Docker est requis ; le SDK .NET et PostgreSQL tournent dans leurs images officielles, épinglées par empreinte
([`docs/toolchain.md`](docs/toolchain.md)).

```
scripts/dev.sh db-up
scripts/dev.sh dotnet run --project src/Deskbook.Cli -- db init
scripts/dev.sh dotnet run --project src/Deskbook.Cli -- rates fetch --from 2026-11-02 --to 2026-11-23
scripts/dev.sh dotnet run --project src/Deskbook.Cli -- trades import samples/trades-fictional.csv
scripts/dev.sh dotnet run --project src/Deskbook.Cli -- book --json
scripts/dev.sh dotnet run --project src/Deskbook.Cli -- recon run samples/confirmations-fictional.csv
scripts/dev.sh dotnet run --project src/Deskbook.Cli -- yields fetch --from 2026-11-02 --to 2026-11-20
scripts/dev.sh dotnet run --project src/Deskbook.Cli -- bonds import samples/bonds-fictional.csv
scripts/dev.sh dotnet run --project src/Deskbook.Cli -- bonds trades import samples/bond-trades-fictional.csv
scripts/dev.sh dotnet run --project src/Deskbook.Cli -- bonds book
scripts/dev.sh dotnet test
scripts/dev.sh web <adresse>:<port>      # la page, publiée sur cette adresse seulement
```

## Comment le code est validé

> Built with Claude Code under the repository owner's direction.

Aucune personne n'a relu le code ligne par ligne. Ce qui le valide :

- **Prix d'obligations contre une implémentation indépendante :** les prix, coupons courus, DV01 et livres
  d'obligations attendus par les tests viennent d'un script Python séparé appliquant la même convention, pas du C#.
- **Tests unitaires** (`tests/Deskbook.Tests`) : l'arithmétique du livre contre des calculs faits à la main à partir
  de taux réels, la décomposition du résultat, chaque cas de refus, la lecture des réponses Valet et des fichiers
  d'opérations.
- **Tests d'intégration contre un vrai PostgreSQL** (`tests/Deskbook.IntegrationTests`) : le stockage, les
  révisions de taux, l'import idempotent, le délai du cache, la ligne de commande de bout en bout, et la page web
  servie sur un vrai port. Ils échouent, sans jamais être
  ignorés, si la base est injoignable. Seule la Banque du Canada est remplacée, par une réponse Valet réelle
  enregistrée : aucun test n'utilise le réseau.
- **Des tests qui peuvent échouer :** `scripts/check-tests-can-fail.sh` fausse une règle à la fois (signe de la
  réévaluation, opération nouvelle, trou dans les taux, taux manquant, révision ignorée, cache périmé, écart de
  montant non signalé, piste d'audit modifiable, échec masqué par la tâche du soir, calendrier des taux faussé par les rendements,
  actualisation décalée d'une période, coupons oubliés, effet de conversion de signe inversé) et exige que les tests
  échouent à chaque fois.

## Données

Taux de change : Banque du Canada, API Valet (https://www.bankofcanada.ca/valet). Taux indicatifs. Reproduits sans
modification de valeur ; sélection de séries et de dates. La Banque du Canada n'appuie pas ce projet. Conditions
lues et consignées dans [`docs/data-licences.md`](docs/data-licences.md).

---

## English

The book of a **fictional** currency desk: spot trades go in; positions per currency and a daily profit and loss in
Canadian dollars come out, valued at the real exchange rates the Bank of Canada publishes. C# on .NET 10,
PostgreSQL 18, a command line with JSON output.

The desk, its counterparties, bonds, trades and positions are invented. Only the market data is real: Bank of
Canada exchange rates and benchmark bond yields.

### What it does

The session above shows it: fetch rates, import trades, print the book.

- **The day's P&L** = book value − value on the previous published day, split into new-trade P&L and revaluation
  of the previous day's positions; the tests check the two parts sum to the total.
- **Refuse rather than guess:** no book for a day with no published rates (2026-11-11, a holiday), for a currency
  with no rate, or when the stored rates leave a hole. Exit code 3 and the reason.
- **Reproducible:** rerunning the book gives the same output, byte for byte.
- **Revised rates:** a value the Bank changes is updated and the old one kept.
- **Bond book:** fictional federal-style bonds, valued daily from the Bank of Canada's benchmark yields (2 to 10
  years, interpolated), with accrued interest, clean price, DV01 and the day's P&L (held positions + new trades).
  It is a model price, labelled as one; a maturity outside the curve is refused, never extrapolated.
  `deskbook yields fetch`, `deskbook bonds book`.
- **A second reporting currency:** `--currency USD` (or any published currency) adds to the CAD book its value
  and P&L in that currency, at each day's rate. The P&L gains one part there: the translation effect (the previous
  value re-marked at the rate change); the parts always add up to the total.
- **Reconciliation:** `deskbook recon run <confirmations.csv>` compares the desk's trades with (fictional)
  counterparty confirmations and lists the breaks: missing confirmation, unknown trade, duplicate, a differing
  field with both values. Every run is kept with the SHA-256 of the file it read; a break collects resolution
  notes. These tables, like the trades table, accept only inserts: the database refuses any update or delete.
- **Deployment and evening job:** a Docker image (SDK to build, ASP.NET runtime image to run), `deploy/compose.yaml`,
  and a systemd timer that runs `deskbook nightly` every weekday evening and alerts on any failure. See
  [`docs/runbook.md`](docs/runbook.md).
- `--json` on every command, for a script or an agent. Exit codes: 0 done, 1 failure, 2 usage, 3 refused,
  4 reconciliation finished with breaks.
- **Read-only web pages:** the FX book, the bond book with the benchmark curve, and the latest reconciliation's
  breaks. A ribbon shows each published day's P&L; each bar leads to that day. The currency choice shows the book
  in a second currency. JSON at `/api/book` and `/api/bonds`, identical to the CLI. A refused day is shown as a
  refusal.

The rules and why: [`docs/problem.md`](docs/problem.md). The order to read the code in:
[`docs/reading-path.md`](docs/reading-path.md). Installing and running it: [`docs/runbook.md`](docs/runbook.md).

### Run it

Only Docker is needed; the .NET SDK and PostgreSQL run in their official images, pinned by digest
([`docs/toolchain.md`](docs/toolchain.md)). The commands are in the French section above.

### How the code is validated

> Built with Claude Code under the repository owner's direction.

No person reviewed the code line by line. What validates it:

- **Bond prices against an independent implementation:** the prices, accrued interest, DV01 and bond books the
  tests expect come from a separate Python script applying the same convention, not from the C#.
- **Unit tests** (`tests/Deskbook.Tests`): the book's arithmetic against hand calculations from real rates, the
  P&L split, every refusal case, reading Valet answers and trade files.
- **Integration tests against a real PostgreSQL** (`tests/Deskbook.IntegrationTests`): storage, rate revisions,
  idempotent import, the cache's timing, the command line end to end, and the web page served on a real port. They fail, never skip, when the database is unreachable.
  Only the Bank of Canada is replaced, by a saved real Valet answer: no test uses the network.
- **Tests that can fail:** `scripts/check-tests-can-fail.sh` breaks one rule at a time (revaluation sign, new-trade
  rule, hole in the rates, missing rate, ignored revision, stale cache, unreported amount break, editable audit
  trail, failure hidden by the evening job, exchange-rate calendar fooled by yields, discounting
  one period off, coupons forgotten, translation effect with the wrong sign) and requires the tests to fail each time.

### Data

Exchange rates: Bank of Canada, Valet API (https://www.bankofcanada.ca/valet). Indicative rates. Values reproduced
unchanged; series and dates selected. The Bank of Canada does not endorse this project. Terms read and recorded in
[`docs/data-licences.md`](docs/data-licences.md).
