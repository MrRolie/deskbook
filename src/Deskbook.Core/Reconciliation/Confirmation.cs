namespace Deskbook.Core.Reconciliation;

/// <summary>
/// A counterparty's confirmation of a trade, as received (fictional). It quotes the desk's trade id and states the
/// trade from the desk's side: what the desk bought and what it sold. Reconciliation compares it with the desk's
/// own record of the trade.
/// </summary>
public sealed record Confirmation(
    string ConfirmationId,
    string TradeId,
    DateOnly TradeDate,
    string Counterparty,
    Leg Bought,
    Leg Sold);
