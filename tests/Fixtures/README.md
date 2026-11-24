# Test fixtures

`valet-fx-2026-11.json`: exchange rates from the Bank of Canada, Valet API
(https://www.bankofcanada.ca/valet), group `FX_RATES_DAILY`, fetched 2026-11-23 (Eastern time). Indicative rates.
**Changed from the original:** trimmed to six series (USD, EUR, GBP, JPY, CHF, RUB) and to the days from
2026-11-02 to 2026-11-23; the values are unchanged. RUB is kept because it carries no value on any day, and
2026-11-11 is absent because the Bank published nothing that day: both are cases the tests need. The Bank of
Canada does not endorse this project. Terms: https://www.bankofcanada.ca/terms/ (see `docs/data-licences.md`).

`trades-fictional.csv` in `samples/` is the matching set of invented trades.

`valet-bond-yields-2026-11.json`: benchmark bond yields from the Bank of Canada, Valet API, group
`bond_yields_benchmark`, fetched 2026-11-24 (UTC). **Changed from the original:** trimmed to the 2, 3, 5, 7 and
10-year series (the long-term and real-return series are dropped) and to 2026-11-02 to 2026-11-20, the last day
published at the fetch; values unchanged. The Bank of Canada does not endorse this project.

`bonds-fictional.csv` and `bond-trades-fictional.csv` in `samples/` are the matching invented bonds and trades.
