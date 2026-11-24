namespace Deskbook.Core;

/// <summary>
/// Reads trades from a CSV file with this exact header:
/// <c>id,trade_date,counterparty,bought_currency,bought_amount,sold_currency,sold_amount</c>
/// (see <see cref="StrictCsv"/> for the rules every line follows).
/// </summary>
public static class TradeCsv
{
    public const string Header = "id,trade_date,counterparty,bought_currency,bought_amount,sold_currency,sold_amount";

    public static IReadOnlyList<Trade> Parse(TextReader reader) =>
        StrictCsv.Read(reader, Header, f => new Trade(
            id: f[0],
            tradeDate: StrictCsv.Date(f[1]),
            counterparty: f[2],
            bought: new Leg(new Currency(f[3].Trim()), StrictCsv.Amount(f[4])),
            sold: new Leg(new Currency(f[5].Trim()), StrictCsv.Amount(f[6]))),
            t => t.Id);
}
