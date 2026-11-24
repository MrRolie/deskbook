# Le problème / The problem

Ce que le prototype jetable (branche `spike/first-book`, jamais fusionnée) a appris, et les décisions que la
première version en tire. La version française fait foi ; l'anglais suit.

What the throwaway spike (branch `spike/first-book`, never merged) taught, and the decisions the first version
takes from it. The French is the reference; the English follows.

## Ce que le prototype a montré

1. **La source.** Le groupe Valet `FX_RATES_DAILY` rend, par jour publié, une valeur par série `FX<devise>CAD` :
   le prix d'une unité de devise en dollars canadiens. Les valeurs sont des chaînes (`"1.4369"`) dont le nombre de
   décimales est fixe pour chaque série : quatre (USD, EUR), cinq (INR, MXN) ou six (JPY, KRW, IDR), soit de deux à
   cinq chiffres significatifs. Les dates sont `yyyy-MM-dd`. Aucune clé d'accès.
2. **Des trous réels.** Le 2026-11-11 n'a aucune observation (jour férié pour la Banque). Trois séries déclarées
   (`RUB`, `SAR`, `VND`) n'ont aucune valeur sur la période. Une absence est donc normale et doit se voir.
3. **L'ordre n'est pas garanti.** Avec `recent=N`, les observations arrivent de la plus récente à la plus
   ancienne ; avec `start_date`, de la plus ancienne à la plus récente. On trie par date, jamais par position.
4. **La précision.** `decimal` en C#, `numeric` en PostgreSQL, lecture avec `CultureInfo.InvariantCulture` :
   rien ne passe par un `double`, et `numeric` garde l'échelle publiée (`0.008830`).
5. **Le grain du yen.** `FXJPYCAD` est publié à six décimales (`0.009030`), mais la sixième valait 0 les 20 jours
   observés (du 2026-10-26 au 2026-11-23) : le pas effectif est 0,00001, soit 1 000 CAD sur 100 millions de yens.
   Une partie du résultat quotidien est ce grain, pas le marché ; deux jours au même taux donnent une
   réévaluation nulle.
6. **Le calcul tient.** Trois opérations fictives, évaluées aux taux réels du 2 au 23 novembre, donnent un livre
   qu'un calcul à la main confirme (le 23 : 350 000 × 1,4369 − 500 000 × 1,5672 + 100 000 000 × 0,009030
   − 590 000 = 32 315 CAD).
7. **L'outillage.** Docker est installé en paquet snap : son `/tmp` est privé, donc l'état de développement vit
   dans un dossier ignoré du dépôt. Npgsql lit et écrit `DateOnly` et `decimal` directement.

## Décisions de la première version

- **Une opération** est un échange au comptant entre deux devises : achetée (montant positif) et vendue
  (montant positif). Les deux jambes comptent, y compris la jambe en CAD. Le résultat est reconnu à la date
  d'opération ; la date de valeur (règlement) est hors du modèle en première version.
- **Une position** par devise est la somme signée des jambes des opérations jusqu'à la date du livre.
- **La valeur du livre** en CAD à la date *d* est la somme de chaque position multipliée par le taux publié pour
  *d* (le CAD vaut 1).
- **Le résultat du jour** est valeur(*d*) − valeur(jour publié précédent). Il se décompose en deux parts dont la
  somme est vérifiée par les tests : le résultat des opérations nouvelles (évaluées au taux de *d*) et la
  réévaluation des positions de la veille (position × variation du taux).
- **Refuser plutôt que deviner.** Un livre n'est produit que pour une date publiée, avec un taux pour chaque
  devise détenue, et seulement si les taux stockés couvrent sans trou l'intervalle depuis le jour publié
  précédent (on distingue un jour férié d'une requête jamais faite en notant chaque intervalle récupéré).
  Sinon, la commande refuse, dit ce qui manque et sort avec un code non nul. Aucun report silencieux d'un taux.
- **Taux révisés.** Si une nouvelle requête rend une autre valeur pour un jour déjà stocké, l'ancienne valeur est
  conservée dans un historique et la révision est signalée.
- **Reproductible.** Le livre est une fonction des opérations et des taux stockés : le relancer donne le même
  résultat, octet pour octet. Les sommes se font sans arrondi ; l'arrondi à deux décimales est fait à l'affichage.
- **Fictif et marqué.** Pupitre, contreparties et opérations sont inventés ; la sortie JSON porte
  `"fictional": true` et la mention de la source.
- **Interface.** Une ligne de commande, sortie JSON dès le premier jour ; codes de sortie distincts pour une
  erreur d'usage, un refus pour données manquantes et une panne.

## Ce qui attendait, et ce qui a été construit depuis

Construits après la première version : une page web, un guide d'exploitation et une tâche du soir avec ses
fichiers de déploiement, un rapprochement avec piste d'audit, un livre d'obligations à partir des rendements de
référence, une seconde devise de présentation. Attend encore : un calendrier des jours fériés tenu à part (les jours
sans publication de la Banque en tiennent lieu aujourd'hui) et le CORRA, qui existe dans Valet.

---

## What the spike showed

1. **The source.** The Valet group `FX_RATES_DAILY` returns, per published day, one value per `FX<currency>CAD`
   series: the price of one unit of the currency in Canadian dollars. Values are strings (`"1.4369"`) with a fixed
   number of decimals per series: four (USD, EUR), five (INR, MXN) or six (JPY, KRW, IDR), which is two to five
   significant figures. Dates are `yyyy-MM-dd`. No access key.
2. **Real gaps.** 2026-11-11 has no observation (a Bank holiday). Three declared series (`RUB`, `SAR`, `VND`) carry
   no value over the period. Absence is normal and must be visible.
3. **Order is not guaranteed.** With `recent=N` observations come newest first; with `start_date`, oldest first.
   Sort by date, never index by position.
4. **Precision.** `decimal` in C#, `numeric` in PostgreSQL, parsing with `CultureInfo.InvariantCulture`: nothing
   goes through a `double`, and `numeric` keeps the published scale (`0.008830`).
5. **The yen's grain.** `FXJPYCAD` is published to six decimals (`0.009030`), but the sixth was 0 on all 20 days
   observed (2026-10-26 to 2026-11-23): the effective step is 0.00001, CAD 1,000 on 100 million yen. Part of the
   daily result is that grain, not the market; two days at the same rate give a revaluation of zero.
6. **The arithmetic holds.** Three fictional trades valued at real rates from 2 to 23 November give a book a hand
   calculation confirms (on the 23rd: 350,000 × 1.4369 − 500,000 × 1.5672 + 100,000,000 × 0.009030 − 590,000 =
   CAD 32,315).
7. **Tooling.** Docker is installed as a snap package: its `/tmp` is private, so development state lives in an
   ignored folder of the repository. Npgsql reads and writes `DateOnly` and `decimal` directly.

## First-version decisions

- **A trade** is a spot exchange between two currencies: bought (positive amount) and sold (positive amount).
  Both legs count, including the CAD leg. Profit and loss is recognised on trade date; value date (settlement)
  is outside the first version's model.
- **A position** per currency is the signed sum of the trades' legs up to the book date.
- **The book's value** in CAD on date *d* is the sum of each position times the rate published for *d* (CAD is 1).
- **The day's P&L** is value(*d*) − value(previous published day). It splits into two parts whose sum the tests
  check: new-trade P&L (new trades valued at *d*'s rate) and revaluation of the previous day's positions
  (position × rate change).
- **Refuse rather than guess.** A book is produced only for a published date, with a rate for every currency held,
  and only if the stored rates cover the interval since the previous published day with no hole (a holiday is told
  apart from a fetch never made by recording every interval fetched). Otherwise the command refuses, says what is
  missing and exits non-zero. No rate is carried forward silently.
- **Revised rates.** If a new fetch returns a different value for a day already stored, the old value is kept in a
  history and the revision is reported.
- **Reproducible.** The book is a function of the stored trades and rates: rerunning it gives the same output,
  byte for byte. Sums are unrounded; rounding to two decimals happens at display.
- **Fictional and marked.** Desk, counterparties and trades are invented; the JSON output carries
  `"fictional": true` and the source attribution.
- **Interface.** A command line with JSON output from day one; distinct exit codes for a usage error, a refusal
  for missing data, and a failure.

## What waited, and what was built since

Built after the first version: a web page, a runbook and an evening job with its deployment files, a
reconciliation with an audit trail, a bond book from the benchmark yields, a second reporting currency. Still
waiting: a holiday calendar kept on its own (the Bank's days without publication stand in for it today) and CORRA,
which exists in Valet.
