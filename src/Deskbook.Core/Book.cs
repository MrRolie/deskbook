namespace Deskbook.Core;

/// <summary>One currency's line in the book. Every amount ending in "Cad" is in Canadian dollars.</summary>
public sealed record PositionLine(
    Currency Currency,
    decimal OpeningAmount,
    decimal ClosingAmount,
    decimal? PreviousRate,
    decimal Rate,
    decimal ValueCad,
    decimal RevaluationPnlCad,
    decimal NewTradesPnlCad);

/// <summary>
/// The desk's book for one published day: positions at close, their value in CAD, and the day's profit and loss,
/// split into what the day's new trades made and what revaluing yesterday's positions made.
/// </summary>
public sealed record Book(
    DateOnly Date,
    DateOnly? PreviousDate,
    IReadOnlyList<PositionLine> Positions,
    IReadOnlyList<string> NewTradeIds,
    decimal ValueCad,
    decimal PreviousValueCad,
    decimal NewTradesPnlCad,
    decimal RevaluationPnlCad)
{
    /// <summary>The day's P&amp;L: the change in the book's value since the previous published day.</summary>
    public decimal TotalPnlCad => ValueCad - PreviousValueCad;
}
