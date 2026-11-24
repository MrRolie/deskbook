using System.Globalization;

namespace Deskbook.Core;

/// <summary>
/// Reads trades from a CSV file with this exact header:
/// <c>id,trade_date,counterparty,bought_currency,bought_amount,sold_currency,sold_amount</c>.
/// Lines starting with # and blank lines are skipped. Dates are yyyy-MM-dd, amounts use a dot as the decimal
/// separator, and fields hold no commas or quotes.
/// Every problem is reported with its line number; nothing is imported from a file with any problem.
/// </summary>
public static class TradeCsv
{
    public const string Header = "id,trade_date,counterparty,bought_currency,bought_amount,sold_currency,sold_amount";

    public static IReadOnlyList<Trade> Parse(TextReader reader)
    {
        var trades = new List<Trade>();
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        var lineNumber = 0;
        var seenHeader = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Trim().Length == 0 || line.StartsWith('#')) continue;
            if (!seenHeader)
            {
                if (line.Trim() != Header) throw new FormatException($"line {lineNumber}: expected the header '{Header}'");
                seenHeader = true;
                continue;
            }
            try
            {
                var trade = ParseLine(line);
                if (!ids.Add(trade.Id)) throw new FormatException($"trade id '{trade.Id}' appears twice in the file");
                trades.Add(trade);
            }
            catch (Exception e) when (e is FormatException or ArgumentException)
            {
                errors.Add($"line {lineNumber}: {e.Message}");
            }
        }

        if (!seenHeader) throw new FormatException($"the file has no header line '{Header}'");
        if (errors.Count > 0) throw new FormatException(string.Join(Environment.NewLine, errors));
        return trades;
    }

    private static Trade ParseLine(string line)
    {
        if (line.Contains('"')) throw new FormatException("quoted fields are not supported");
        var f = line.Split(',');
        if (f.Length != 7) throw new FormatException($"expected 7 fields, found {f.Length}");

        return new Trade(
            id: f[0],
            tradeDate: DateOnly.ParseExact(f[1].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            counterparty: f[2],
            bought: new Leg(new Currency(f[3].Trim()), ParseAmount(f[4])),
            sold: new Leg(new Currency(f[5].Trim()), ParseAmount(f[6])));
    }

    private static decimal ParseAmount(string text) =>
        decimal.Parse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}
