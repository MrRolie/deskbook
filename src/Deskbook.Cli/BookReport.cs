using System.Text;
using Deskbook.Core;

namespace Deskbook.Cli;

/// <summary>The book as the command line shows it: a JSON document or a text table.</summary>
public static class BookReport
{
    public sealed record PositionJson(
        string Currency, decimal OpeningAmount, decimal ClosingAmount, decimal? PreviousCadPerUnit, decimal CadPerUnit,
        decimal ValueCad, decimal RevaluationPnlCad, decimal NewTradesPnlCad);

    public sealed record PnlJson(decimal NewTradesCad, decimal RevaluationCad, decimal TotalCad);

    public sealed record BookJson(
        bool Fictional, string Desk, string BookDate, string? PreviousDate, string ReportingCurrency,
        IReadOnlyList<PositionJson> Positions, IReadOnlyList<string> NewTrades,
        decimal ValueCad, decimal PreviousValueCad, PnlJson Pnl, string Note, string Source);

    public static BookJson ToJson(Book book) => new(
        Fictional: true,
        Desk: Attribution.Desk,
        BookDate: Output.Date(book.Date),
        PreviousDate: book.PreviousDate is null ? null : Output.Date(book.PreviousDate.Value),
        ReportingCurrency: Currency.Cad.Code,
        Positions: book.Positions.Select(p => new PositionJson(
            p.Currency.Code, p.OpeningAmount, p.ClosingAmount, p.PreviousRate, p.Rate,
            Output.Cents(p.ValueCad), Output.Cents(p.RevaluationPnlCad), Output.Cents(p.NewTradesPnlCad))).ToList(),
        NewTrades: book.NewTradeIds,
        ValueCad: Output.Cents(book.ValueCad),
        PreviousValueCad: Output.Cents(book.PreviousValueCad),
        Pnl: new PnlJson(Output.Cents(book.NewTradesPnlCad), Output.Cents(book.RevaluationPnlCad), Output.Cents(book.TotalPnlCad)),
        Note: Attribution.Fictional,
        Source: Attribution.Source);

    public static string ToText(Book book)
    {
        var text = new StringBuilder();
        var previous = book.PreviousDate is null ? "none" : Output.Date(book.PreviousDate.Value);
        text.AppendLine($"{Attribution.Desk}: book for {Output.Date(book.Date)} (previous published day: {previous})");
        text.AppendLine();
        text.AppendLine($"{"Ccy",-4} {"Position",18} {"CAD per unit",13} {"Value CAD",15} {"Reval P&L",12} {"New trades P&L",15}");
        foreach (var p in book.Positions)
        {
            text.AppendLine(
                $"{p.Currency.Code,-4} {Output.Amount(p.ClosingAmount),18} {p.Rate,13} {Output.Amount(Output.Cents(p.ValueCad)),15} " +
                $"{Output.Amount(Output.Cents(p.RevaluationPnlCad)),12} {Output.Amount(Output.Cents(p.NewTradesPnlCad)),15}");
        }
        text.AppendLine();
        text.AppendLine($"Book value: CAD {Output.Amount(Output.Cents(book.ValueCad))} " +
                        $"(previous CAD {Output.Amount(Output.Cents(book.PreviousValueCad))})");
        text.AppendLine($"Day's P&L:  CAD {Output.Amount(Output.Cents(book.TotalPnlCad))} = new trades " +
                        $"{Output.Amount(Output.Cents(book.NewTradesPnlCad))} + revaluation {Output.Amount(Output.Cents(book.RevaluationPnlCad))}");
        if (book.NewTradeIds.Count > 0) text.AppendLine("New trades: " + string.Join(", ", book.NewTradeIds));
        text.AppendLine();
        text.AppendLine(Attribution.Fictional);
        text.AppendLine(Attribution.Source);
        return text.ToString();
    }
}
