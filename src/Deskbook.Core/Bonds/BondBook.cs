namespace Deskbook.Core.Bonds;

/// <summary>One bond's line in the bond book. Amounts ending in "Cad" are Canadian dollars; prices are per 100 of face.</summary>
public sealed record BondLine(
    Bond Bond,
    decimal OpeningFace,
    decimal ClosingFace,
    BondPrice? Price,
    decimal MarketValueCad,
    decimal Dv01Cad,
    decimal HeldPnlCad,
    decimal NewTradesPnlCad);

/// <summary>
/// The desk's bond book for one published day. Book value = market value of the bonds held (full price) + the cash the
/// bond trades have paid and received (purchases, sales, coupons, redemptions), so that buying a bond at a fair price
/// changes nothing and the day's P&amp;L is the change in book value.
/// </summary>
public sealed record BondBook(
    DateOnly Date,
    DateOnly? PreviousDate,
    IReadOnlyList<BondLine> Lines,
    IReadOnlyList<string> NewTradeIds,
    decimal MarketValueCad,
    decimal CashCad,
    decimal PreviousValueCad,
    decimal Dv01Cad,
    decimal HeldPnlCad,
    decimal NewTradesPnlCad)
{
    public decimal ValueCad => MarketValueCad + CashCad;
    public decimal TotalPnlCad => ValueCad - PreviousValueCad;
}

/// <summary>Builds a <see cref="BondBook"/> from two days of yield curves, the bonds and the bond trades. Pure, like the FX book.</summary>
public static class BondBookCalculator
{
    public static BondBook Compute(YieldCurve today, YieldCurve? previous, IReadOnlyDictionary<string, Bond> bonds, IEnumerable<BondTrade> trades)
    {
        var date = today.Date;
        var upToDate = trades.Where(t => t.TradeDate <= date).ToList();
        if (previous is null && upToDate.Any(t => t.TradeDate < date))
        {
            throw new BookRefusedException($"bond trades exist before {date:yyyy-MM-dd} but no earlier yields are stored to value them");
        }
        bool IsNew(BondTrade t) => previous is null || t.TradeDate > previous.Date;

        var missing = new List<string>();
        foreach (var t in upToDate.Where(t => !bonds.ContainsKey(t.BondId))) missing.Add($"trade {t.Id} is in bond {t.BondId}, which is not defined");
        if (missing.Count > 0) throw new BookRefusedException(missing);

        var lines = new List<BondLine>();
        foreach (var group in upToDate.GroupBy(t => t.BondId).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var bond = bonds[group.Key];
            var openingFace = group.Where(t => !IsNew(t)).Sum(t => t.Face);
            var newTrades = group.Where(IsNew).ToList();
            var closingFace = openingFace + newTrades.Sum(t => t.Face);
            var alive = date < bond.Maturity;

            BondPrice? price = null;
            if (alive && (closingFace != 0 || newTrades.Count > 0))
            {
                if (today.TryYieldFor(bond.Maturity, out var y, out var reason)) price = BondPricer.Price(bond, date, y);
                else missing.Add($"bond {bond.Id}: {reason}");
            }

            BondPrice? previousPrice = null;
            if (previous is not null && openingFace != 0 && previous.Date < bond.Maturity)
            {
                if (previous.TryYieldFor(bond.Maturity, out var y, out var reason)) previousPrice = BondPricer.Price(bond, previous.Date, y);
                else missing.Add($"bond {bond.Id} on {previous.Date:yyyy-MM-dd}: {reason}");
            }
            if (missing.Count > 0) continue;

            var fullToday = alive ? price?.FullPrice ?? 0m : 0m;
            var fullBefore = previousPrice?.FullPrice ?? 0m;

            // Held: price change of what was held at the previous close, plus what it paid in between.
            var held = previous is null ? 0m
                : openingFace / 100m * (fullToday - fullBefore + bond.CashFlowsPer100(previous.Date, date));
            // New: value now, minus what was paid (clean price + accrued on the trade date), plus what it paid since.
            var fresh = newTrades.Sum(t =>
                t.Face / 100m * (fullToday - (t.CleanPrice + BondPricer.Accrued(bond, t.TradeDate)) + bond.CashFlowsPer100(t.TradeDate, date)));

            lines.Add(new BondLine(bond, openingFace, alive ? closingFace : 0m, price,
                MarketValueCad: alive ? closingFace / 100m * fullToday : 0m,
                Dv01Cad: alive && price is not null ? closingFace / 100m * price.Dv01Per100 : 0m,
                HeldPnlCad: held,
                NewTradesPnlCad: fresh));
        }
        if (missing.Count > 0) throw new BookRefusedException(missing);

        var cash = upToDate.Sum(t => Cash(t, bonds[t.BondId], date));
        var previousValue = previous is null ? 0m : PreviousValue(previous, bonds, upToDate.Where(t => !IsNew(t)).ToList());

        return new BondBook(date, previous?.Date, lines,
            upToDate.Where(IsNew).Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal).ToList(),
            MarketValueCad: lines.Sum(l => l.MarketValueCad),
            CashCad: cash,
            PreviousValueCad: previousValue,
            Dv01Cad: lines.Sum(l => l.Dv01Cad),
            HeldPnlCad: lines.Sum(l => l.HeldPnlCad),
            NewTradesPnlCad: lines.Sum(l => l.NewTradesPnlCad));
    }

    /// <summary>The cash a trade has moved by <paramref name="date"/>: minus what was paid, plus coupons and principal received.</summary>
    private static decimal Cash(BondTrade t, Bond bond, DateOnly date) =>
        t.Face / 100m * (bond.CashFlowsPer100(t.TradeDate, date) - (t.CleanPrice + BondPricer.Accrued(bond, t.TradeDate)));

    private static decimal PreviousValue(YieldCurve previous, IReadOnlyDictionary<string, Bond> bonds, IReadOnlyList<BondTrade> held)
    {
        var value = held.Sum(t => Cash(t, bonds[t.BondId], previous.Date));
        foreach (var group in held.GroupBy(t => t.BondId))
        {
            var bond = bonds[group.Key];
            var face = group.Sum(t => t.Face);
            if (face == 0 || previous.Date >= bond.Maturity) continue;
            previous.TryYieldFor(bond.Maturity, out var y, out _);
            value += face / 100m * BondPricer.Price(bond, previous.Date, y).FullPrice;
        }
        return value;
    }
}
