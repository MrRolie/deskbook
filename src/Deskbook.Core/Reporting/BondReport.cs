using System.Globalization;
using System.Text;
using Deskbook.Core.Bonds;

namespace Deskbook.Core.Reporting;

/// <summary>The bond book as JSON-ready records and as a text table. Prices per 100 of face are shown to 4 decimals.</summary>
public static class BondReport
{
    public sealed record LineJson(string BondId, string Description, decimal CouponPercent, string Maturity, decimal OpeningFace,
        decimal Face, decimal? YieldPercent, decimal? CleanPrice, decimal? Accrued, decimal? FullPrice, decimal MarketValueCad,
        decimal Dv01Cad, decimal HeldPnlCad, decimal NewTradesPnlCad);

    public sealed record PnlJson(decimal HeldCad, decimal NewTradesCad, decimal TotalCad);

    public sealed record BondBookJson(bool Fictional, string Desk, string BookDate, string? PreviousDate, string Model,
        IReadOnlyList<LineJson> Bonds, IReadOnlyList<string> NewTrades, decimal MarketValueCad, decimal CashCad, decimal ValueCad,
        decimal PreviousValueCad, decimal Dv01Cad, PnlJson Pnl, string Note, string Source);

    public const string Model =
        "Model prices from Bank of Canada benchmark yields (2, 3, 5, 7, 10 years), interpolated linearly in years to maturity; " +
        "semi-annual street convention; accrued actual/actual in the coupon period. Not market quotes.";

    public const string Source =
        "Benchmark bond yields: Bank of Canada, Valet API (https://www.bankofcanada.ca/valet), group bond_yields_benchmark. " +
        "Values reproduced unchanged; series and dates selected; prices are derived by this project. The Bank of Canada does not endorse this project.";

    private static decimal P(decimal price) => Math.Round(price, 4, MidpointRounding.AwayFromZero);

    public static BondBookJson ToJson(BondBook book) => new(
        Fictional: true,
        Desk: Attribution.Desk,
        BookDate: Output.Date(book.Date),
        PreviousDate: book.PreviousDate is null ? null : Output.Date(book.PreviousDate.Value),
        Model: Model,
        Bonds: book.Lines.Select(l => new LineJson(l.Bond.Id, l.Bond.Description, l.Bond.CouponPercent, Output.Date(l.Bond.Maturity),
            l.OpeningFace, l.ClosingFace,
            l.Price is null ? null : Math.Round(l.Price.YieldPercent, 4, MidpointRounding.AwayFromZero),
            l.Price is null ? null : P(l.Price.CleanPrice), l.Price is null ? null : P(l.Price.Accrued), l.Price is null ? null : P(l.Price.FullPrice),
            Output.Cents(l.MarketValueCad), Output.Cents(l.Dv01Cad), Output.Cents(l.HeldPnlCad), Output.Cents(l.NewTradesPnlCad))).ToList(),
        NewTrades: book.NewTradeIds,
        MarketValueCad: Output.Cents(book.MarketValueCad),
        CashCad: Output.Cents(book.CashCad),
        ValueCad: Output.Cents(book.ValueCad),
        PreviousValueCad: Output.Cents(book.PreviousValueCad),
        Dv01Cad: Output.Cents(book.Dv01Cad),
        Pnl: new PnlJson(Output.Cents(book.HeldPnlCad), Output.Cents(book.NewTradesPnlCad), Output.Cents(book.TotalPnlCad)),
        Note: Attribution.Fictional,
        Source: Source);

    public static string ToText(BondBook book)
    {
        var text = new StringBuilder();
        var previous = book.PreviousDate is null ? "none" : Output.Date(book.PreviousDate.Value);
        text.AppendLine($"{Attribution.Desk}: bond book for {Output.Date(book.Date)} (previous published day: {previous})");
        text.AppendLine();
        text.AppendLine($"{"Bond",-16} {"Face",14} {"Yield %",8} {"Clean",9} {"Accrued",8} {"Value CAD",15} {"DV01 CAD",10} {"Held P&L",11} {"New P&L",10}");
        foreach (var l in book.Lines)
        {
            var y = l.Price is null ? "-" : Math.Round(l.Price.YieldPercent, 4).ToString("0.0000", CultureInfo.InvariantCulture);
            var clean = l.Price is null ? "-" : P(l.Price.CleanPrice).ToString("0.0000", CultureInfo.InvariantCulture);
            var acc = l.Price is null ? "-" : P(l.Price.Accrued).ToString("0.0000", CultureInfo.InvariantCulture);
            text.AppendLine($"{l.Bond.Id,-16} {Output.Amount(l.ClosingFace),14} {y,8} {clean,9} {acc,8} {Output.Amount(Output.Cents(l.MarketValueCad)),15} " +
                            $"{Output.Amount(Output.Cents(l.Dv01Cad)),10} {Output.Amount(Output.Cents(l.HeldPnlCad)),11} {Output.Amount(Output.Cents(l.NewTradesPnlCad)),10}");
        }
        text.AppendLine();
        text.AppendLine($"Bonds at market: CAD {Output.Amount(Output.Cents(book.MarketValueCad))}; cash from bond trades: CAD {Output.Amount(Output.Cents(book.CashCad))}");
        text.AppendLine($"Book value: CAD {Output.Amount(Output.Cents(book.ValueCad))} (previous CAD {Output.Amount(Output.Cents(book.PreviousValueCad))}); DV01 CAD {Output.Amount(Output.Cents(book.Dv01Cad))}");
        text.AppendLine($"Day's P&L:  CAD {Output.Amount(Output.Cents(book.TotalPnlCad))} = held {Output.Amount(Output.Cents(book.HeldPnlCad))} + new trades {Output.Amount(Output.Cents(book.NewTradesPnlCad))}");
        if (book.NewTradeIds.Count > 0) text.AppendLine("New trades: " + string.Join(", ", book.NewTradeIds));
        text.AppendLine();
        text.AppendLine(Model);
        text.AppendLine(Attribution.Fictional);
        text.AppendLine(Source);
        return text.ToString();
    }
}
