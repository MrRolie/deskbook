using System.Globalization;

namespace Deskbook.Core.Bonds;

/// <summary>Reads bond definitions and bond trades, in the strict CSV dialect of <see cref="StrictCsv"/>.</summary>
public static class BondCsv
{
    public const string BondHeader = "id,description,coupon_percent,maturity";
    public const string TradeHeader = "id,trade_date,bond_id,face,clean_price,counterparty";

    public static IReadOnlyList<Bond> ParseBonds(TextReader reader) =>
        StrictCsv.Read(reader, BondHeader,
            f => new Bond(f[0], f[1], StrictCsv.Amount(f[2]), StrictCsv.Date(f[3])), b => b.Id);

    /// <summary>Face is signed: positive for a purchase, negative for a sale.</summary>
    public static IReadOnlyList<BondTrade> ParseTrades(TextReader reader) =>
        StrictCsv.Read(reader, TradeHeader,
            f => new BondTrade(f[0], StrictCsv.Date(f[1]), f[2],
                decimal.Parse(f[3].Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
                StrictCsv.Amount(f[4]), f[5]),
            t => t.Id);
}
