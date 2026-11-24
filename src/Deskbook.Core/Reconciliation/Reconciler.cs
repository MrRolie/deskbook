using System.Globalization;

namespace Deskbook.Core.Reconciliation;

/// <summary>
/// Compares the desk's trades with counterparty confirmations. Pure: the same two lists always give the same breaks,
/// in the same order. A confirmation is paired with a trade by the trade id it quotes; amounts must agree exactly
/// (a confirmation is a legal record, so there is no tolerance).
/// </summary>
public static class Reconciler
{
    public static ReconciliationResult Reconcile(IReadOnlyList<Trade> trades, IReadOnlyList<Confirmation> confirmations)
    {
        var tradesById = trades.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var confirmed = new HashSet<string>(StringComparer.Ordinal);
        var breaks = new List<Break>();
        var matched = 0;
        var unknown = new List<Confirmation>();

        foreach (var confirmation in confirmations.OrderBy(c => c.ConfirmationId, StringComparer.Ordinal))
        {
            if (!tradesById.TryGetValue(confirmation.TradeId, out var trade))
            {
                unknown.Add(confirmation);
                continue;
            }
            if (!confirmed.Add(trade.Id))
            {
                breaks.Add(new Break(BreakKind.DuplicateConfirmation, trade.Id, confirmation.ConfirmationId, null, null, null,
                    $"trade {trade.Id} is already confirmed by another confirmation"));
                continue;
            }

            var differences = Compare(trade, confirmation).ToList();
            if (differences.Count == 0) matched++;
            breaks.AddRange(differences);
        }

        foreach (var trade in trades.Where(t => !confirmed.Contains(t.Id)))
        {
            breaks.Add(new Break(BreakKind.MissingConfirmation, trade.Id, null, null, null, null,
                $"no confirmation quotes trade {trade.Id} ({trade.Counterparty}, {trade.TradeDate:yyyy-MM-dd})"));
        }

        foreach (var confirmation in unknown)
        {
            // A confirmation with a wrong reference often describes a real trade: name the unconfirmed trades it matches.
            var candidates = trades
                .Where(t => !confirmed.Contains(t.Id) && SameEconomics(t, confirmation))
                .Select(t => t.Id)
                .ToList();
            var hint = candidates.Count == 0 ? "" : $"; same economics as unconfirmed trade(s) {string.Join(", ", candidates)}";
            breaks.Add(new Break(BreakKind.UnknownTrade, null, confirmation.ConfirmationId, null, null, null,
                $"confirmation {confirmation.ConfirmationId} quotes trade '{confirmation.TradeId}', which the desk never booked{hint}"));
        }

        var ordered = breaks
            .OrderBy(b => b.TradeId ?? "￿", StringComparer.Ordinal)
            .ThenBy(b => b.ConfirmationId ?? "", StringComparer.Ordinal)
            .ThenBy(b => b.Kind)
            .ThenBy(b => b.Field ?? "", StringComparer.Ordinal)
            .ToList();
        return new ReconciliationResult(trades.Count, confirmations.Count, matched, ordered);
    }

    private static IEnumerable<Break> Compare(Trade trade, Confirmation confirmation)
    {
        Break? Field(string name, string desk, string counterparty) => desk == counterparty
            ? null
            : new Break(BreakKind.FieldMismatch, trade.Id, confirmation.ConfirmationId, name, desk, counterparty,
                $"{name}: desk has {desk}, counterparty confirms {counterparty}");

        var checks = new[]
        {
            Field("trade_date", Date(trade.TradeDate), Date(confirmation.TradeDate)),
            Field("counterparty", trade.Counterparty, confirmation.Counterparty),
            Field("bought_currency", trade.Bought.Currency.Code, confirmation.Bought.Currency.Code),
            AmountField("bought_amount", trade.Bought.Amount, confirmation.Bought.Amount),
            Field("sold_currency", trade.Sold.Currency.Code, confirmation.Sold.Currency.Code),
            AmountField("sold_amount", trade.Sold.Amount, confirmation.Sold.Amount),
        };
        return checks.Where(b => b is not null)!;

        // Amounts compare as numbers (1000 equals 1000.00) but are shown as written.
        Break? AmountField(string name, decimal desk, decimal counterparty) => desk == counterparty
            ? null
            : Field(name, Amount(desk), Amount(counterparty));
    }

    private static bool SameEconomics(Trade t, Confirmation c) =>
        t.TradeDate == c.TradeDate && t.Bought.Currency == c.Bought.Currency && t.Bought.Amount == c.Bought.Amount
        && t.Sold.Currency == c.Sold.Currency && t.Sold.Amount == c.Sold.Amount;

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Amount(decimal a) => a.ToString(CultureInfo.InvariantCulture);
}
