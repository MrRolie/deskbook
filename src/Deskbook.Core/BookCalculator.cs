namespace Deskbook.Core;

/// <summary>
/// Turns trades and two days of rates into a <see cref="Book"/>. Pure: no database, no clock, no network, so the
/// same inputs always give the same book.
/// </summary>
public static class BookCalculator
{
    /// <param name="today">Rates published on the book date.</param>
    /// <param name="previous">Rates of the previous published day, or null when none is stored.</param>
    /// <param name="trades">Any trades; those dated after the book date are ignored.</param>
    public static Book Compute(RateSet today, RateSet? previous, IEnumerable<Trade> trades)
    {
        var date = today.Date;
        var upToDate = trades.Where(t => t.TradeDate <= date).ToList();

        // A trade is "new" if it was done after the previous published day (a trade dated on a holiday
        // belongs to the next published day).
        bool IsNew(Trade t) => previous is null || t.TradeDate > previous.Date;

        if (previous is null && upToDate.Any(t => t.TradeDate < date))
        {
            throw new BookRefusedException(
                $"trades exist before {date:yyyy-MM-dd} but no earlier rates are stored to value them");
        }

        var opening = SumLegs(upToDate.Where(t => !IsNew(t)));
        var newTrades = upToDate.Where(IsNew).ToList();
        var newLegs = SumLegs(newTrades);

        var currencies = opening.Keys.Union(newLegs.Keys).OrderBy(c => c.Code, StringComparer.Ordinal).ToList();

        var missing = new List<string>();
        var lines = new List<PositionLine>();
        foreach (var currency in currencies)
        {
            var openingAmount = opening.GetValueOrDefault(currency);
            var newAmount = newLegs.GetValueOrDefault(currency);
            var closingAmount = openingAmount + newAmount;
            if (openingAmount == 0 && newAmount == 0) continue; // flat before and no new trades: nothing to value

            if (!today.TryGetCadPerUnit(currency, out var rate))
            {
                missing.Add($"no {currency} rate published for {date:yyyy-MM-dd}");
                continue;
            }

            decimal? previousRate = null;
            if (previous is not null)
            {
                if (previous.TryGetCadPerUnit(currency, out var p)) previousRate = p;
                else if (openingAmount != 0)
                {
                    missing.Add($"no {currency} rate published for {previous.Date:yyyy-MM-dd}");
                    continue;
                }
            }

            var revaluation = openingAmount == 0 ? 0m : openingAmount * (rate - previousRate!.Value);
            lines.Add(new PositionLine(
                currency,
                openingAmount,
                closingAmount,
                previousRate,
                rate,
                ValueCad: closingAmount * rate,
                RevaluationPnlCad: revaluation,
                NewTradesPnlCad: newAmount * rate));
        }

        if (missing.Count > 0) throw new BookRefusedException(missing);

        var previousValue = lines.Sum(l => l.OpeningAmount == 0 ? 0m : l.OpeningAmount * l.PreviousRate!.Value);
        return new Book(
            date,
            previous?.Date,
            lines,
            newTrades.Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal).ToList(),
            ValueCad: lines.Sum(l => l.ValueCad),
            PreviousValueCad: previousValue,
            NewTradesPnlCad: lines.Sum(l => l.NewTradesPnlCad),
            RevaluationPnlCad: lines.Sum(l => l.RevaluationPnlCad));
    }

    private static Dictionary<Currency, decimal> SumLegs(IEnumerable<Trade> trades)
    {
        var sums = new Dictionary<Currency, decimal>();
        foreach (var trade in trades)
        {
            foreach (var (currency, amount) in trade.SignedLegs())
            {
                sums[currency] = sums.GetValueOrDefault(currency) + amount;
            }
        }
        return sums;
    }
}
