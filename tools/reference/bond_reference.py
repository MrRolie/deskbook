"""Independent reference for the bond book's expected numbers.

The bond tests in tests/Deskbook.Tests/BondTests.cs and tests/Deskbook.IntegrationTests/BondBookTests.cs expect
numbers that come from this script, not from the C# they check. It applies the same written convention (semi-annual
street convention, accrued actual/actual within the coupon period, linear interpolation of the benchmark yields in
years of 365.25 days, book value = full-price market value + the trades' cash) from scratch, in plain Python with
the standard library only.

Run from the repository root:
    python3 -I tools/reference/bond_reference.py

It reads tests/Fixtures/valet-bond-yields-2026-11.json and samples/bond-trades-fictional.csv and prints the values
the tests assert. Every bond and trade here is fictional; the yields are the Bank of Canada's.
"""
import calendar
import csv
import json
from datetime import date

FIXTURE = "tests/Fixtures/valet-bond-yields-2026-11.json"
BONDS = "samples/bonds-fictional.csv"
TRADES = "samples/bond-trades-fictional.csv"
TENORS = [2, 3, 5, 7, 10]


def add_months(d, months):
    y, m = divmod(d.month - 1 + months, 12)
    day = min(d.day, calendar.monthrange(d.year + y, m + 1)[1])
    return date(d.year + y, m + 1, day)


def price(coupon, maturity, settle, yield_pct):
    """Full price, accrued and clean price per 100 of face."""
    k = 1
    while add_months(maturity, -6 * k) > settle:
        k += 1
    start, nxt = add_months(maturity, -6 * k), add_months(maturity, -6 * (k - 1))
    w = (nxt - settle).days / (nxt - start).days
    flows = [coupon / 2 + (100 if j == 0 else 0) for j in range(k - 1, -1, -1)]
    full = sum(cf / (1 + yield_pct / 200) ** (i + w) for i, cf in enumerate(flows))
    accrued = coupon / 2 * (settle - start).days / (nxt - start).days
    return full, accrued, full - accrued


def dv01(coupon, maturity, settle, yield_pct):
    return (price(coupon, maturity, settle, yield_pct - 0.01)[0] - price(coupon, maturity, settle, yield_pct + 0.01)[0]) / 2


def rows(path):
    with open(path, newline="") as f:
        return list(csv.DictReader(line for line in f if not line.startswith("#") and line.strip()))


def main():
    print("Single prices (per 100):")
    for coupon, maturity, settle, y in [(3.25, date(2031, 6, 1), date(2026, 11, 23), 3.52),
                                        (4.0, date(2031, 6, 1), date(2026, 12, 1), 4.0),
                                        (2.75, date(2029, 9, 1), date(2026, 11, 20), 3.40)]:
        full, accrued, clean = price(coupon, maturity, settle, y)
        print(f"  {coupon}% {maturity} on {settle} at {y}%: full {full:.8f} accrued {accrued:.8f} "
              f"clean {clean:.8f} DV01 {dv01(coupon, maturity, settle, y):.8f}")

    observations = json.load(open(FIXTURE))["observations"]
    curves = {o["d"]: {int(k.split(".")[2][:-2]): float(v["v"]) for k, v in o.items() if k != "d"} for o in observations}
    bonds = {b["id"]: (float(b["coupon_percent"]), date.fromisoformat(b["maturity"])) for b in rows(BONDS)}
    trades = [(date.fromisoformat(t["trade_date"]), t["bond_id"], float(t["face"]), float(t["clean_price"])) for t in rows(TRADES)]

    def yield_for(day, maturity):
        curve, years = curves[str(day)], (maturity - day).days / 365.25
        lo, hi = max(t for t in TENORS if t <= years), min(t for t in TENORS if t >= years)
        return curve[lo] if lo == hi else curve[lo] + (curve[hi] - curve[lo]) * (years - lo) / (hi - lo)

    def book(day):
        cash = -sum(face / 100 * (clean + price(*bonds[b], td, 3.0)[1]) for td, b, face, clean in trades if td <= day)
        market = risk = 0.0
        for b, (coupon, maturity) in bonds.items():
            face = sum(f for td, bid, f, _ in trades if bid == b and td <= day)
            if face:
                y = yield_for(day, maturity)
                market += face / 100 * price(coupon, maturity, day, y)[0]
                risk += face / 100 * dv01(coupon, maturity, day, y)
        return market + cash, risk

    print("Sample bond book (CAD):")
    for day, previous in [(date(2026, 11, 20), date(2026, 11, 19)), (date(2026, 11, 12), date(2026, 11, 10))]:
        value, risk = book(day)
        before, _ = book(previous)
        print(f"  {day}: value {value:.2f} previous {before:.2f} P&L {value - before:.2f} DV01 {risk:.2f}")


if __name__ == "__main__":
    main()
