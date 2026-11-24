namespace Deskbook.Core;

/// <summary>One side of a trade: an amount of one currency. Amounts on a trade are always positive.</summary>
public sealed record Leg(Currency Currency, decimal Amount);

/// <summary>
/// A spot foreign-exchange trade done by the (fictional) desk: it bought one currency and paid for it in another.
/// Profit and loss is recognised on <see cref="TradeDate"/>; settlement is outside the model.
/// </summary>
public sealed record Trade
{
    public string Id { get; }
    public DateOnly TradeDate { get; }
    public string Counterparty { get; }
    public Leg Bought { get; }
    public Leg Sold { get; }

    public Trade(string id, DateOnly tradeDate, string counterparty, Leg bought, Leg sold)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A trade needs an id.", nameof(id));
        if (string.IsNullOrWhiteSpace(counterparty)) throw new ArgumentException("A trade needs a counterparty.", nameof(counterparty));
        if (bought.Amount <= 0 || sold.Amount <= 0) throw new ArgumentException($"Trade {id}: both amounts must be positive.");
        if (bought.Currency == sold.Currency) throw new ArgumentException($"Trade {id}: bought and sold currencies must differ.");

        Id = id.Trim();
        TradeDate = tradeDate;
        Counterparty = counterparty.Trim();
        Bought = bought;
        Sold = sold;
    }

    /// <summary>The trade's effect on the desk's balances: plus the bought amount, minus the sold amount.</summary>
    public IEnumerable<(Currency Currency, decimal Amount)> SignedLegs()
    {
        yield return (Bought.Currency, Bought.Amount);
        yield return (Sold.Currency, -Sold.Amount);
    }
}
