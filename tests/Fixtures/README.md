# Test fixtures

`valet-fx-2026-11.json`: exchange rates from the Bank of Canada, Valet API
(https://www.bankofcanada.ca/valet), group `FX_RATES_DAILY`, fetched 2026-11-23 (Eastern time). Indicative rates.
**Changed from the original:** trimmed to six series (USD, EUR, GBP, JPY, CHF, RUB) and to the days from
2026-11-02 to 2026-11-23; the values are unchanged. RUB is kept because it carries no value on any day, and
2026-11-11 is absent because the Bank published nothing that day: both are cases the tests need. The Bank of
Canada does not endorse this project. Terms: https://www.bankofcanada.ca/terms/ (see `docs/data-licences.md`).

`trades-fictional.csv` in `samples/` is the matching set of invented trades.
