using Deskbook.Cli;
using Deskbook.Core;
using Deskbook.Infrastructure.Valet;
using static Deskbook.Tests.Build;

namespace Deskbook.Tests;

public class ParsingTests
{
    [Fact]
    public void Valet_days_are_parsed_with_their_published_precision()
    {
        var days = ValetParser.ParseDays(File.ReadAllText(Fixture("valet-fx-2026-11.json")));
        var day = days.Single(d => d.Date == D("2026-11-23"));

        Assert.True(day.TryGetCadPerUnit(new Currency("USD"), out var usd));
        Assert.Equal(1.4369m, usd);
        Assert.True(day.TryGetCadPerUnit(new Currency("JPY"), out var jpy));
        Assert.Equal("0.009030", jpy.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Valet_holidays_and_series_without_values_are_absent_not_zero()
    {
        var days = ValetParser.ParseDays(File.ReadAllText(Fixture("valet-fx-2026-11.json")));

        Assert.Equal(15, days.Count);
        Assert.DoesNotContain(days, d => d.Date == D("2026-11-11"));
        Assert.All(days, d => Assert.False(d.TryGetCadPerUnit(new Currency("RUB"), out _)));
    }

    [Fact]
    public void Valet_days_come_out_oldest_first_whatever_order_they_arrive_in()
    {
        const string newestFirst = """
            {"observations": [
              {"d": "2026-11-20", "FXUSDCAD": {"v": "1.4325"}},
              {"d": "2026-11-19", "FXUSDCAD": {"v": "1.4300"}, "FXRUBCAD": {"v": ""}, "OTHER": {"v": "9"}}
            ]}
            """;
        var days = ValetParser.ParseDays(newestFirst);
        Assert.Equal(new[] { D("2026-11-19"), D("2026-11-20") }, days.Select(d => d.Date));
        Assert.Equal(1, days[0].Count);
    }

    [Fact]
    public void The_sample_trade_file_parses()
    {
        using var csv = new StreamReader(Sample("trades-fictional.csv"));
        var trades = TradeCsv.Parse(csv);
        Assert.Equal(8, trades.Count);
        Assert.All(trades, t => Assert.Contains("(fictional)", t.Counterparty));
    }

    [Fact]
    public void A_trade_file_with_errors_reports_every_bad_line_and_imports_nothing()
    {
        var text = string.Join('\n',
            TradeCsv.Header,
            "T1,2026-11-03,CP (fictional),USD,100,CAD,140",
            "T2,03/11/2026,CP (fictional),USD,100,CAD,140",
            "T3,2026-11-03,CP (fictional),USD,100,USD,140",
            "T1,2026-11-03,CP (fictional),USD,100,CAD,140",
            "T4,2026-11-03,\"CP, quoted\",USD,100,CAD,140");
        var error = Assert.Throws<FormatException>(() => TradeCsv.Parse(new StringReader(text)));
        Assert.Contains("line 3:", error.Message);
        Assert.Contains("line 4:", error.Message);
        Assert.Contains("line 5: id 'T1' appears twice", error.Message);
        Assert.Contains("line 6: quoted fields", error.Message);
        Assert.DoesNotContain("line 2:", error.Message);
    }

    [Fact]
    public void A_trade_file_without_the_header_is_rejected()
    {
        Assert.Throws<FormatException>(() => TradeCsv.Parse(new StringReader("T1,2026-11-03,CP,USD,100,CAD,140")));
    }

    [Fact]
    public void A_comma_amount_is_rejected_rather_than_read_in_another_locale()
    {
        var text = TradeCsv.Header + "\nT1,2026-11-03,CP (fictional),USD,100;5,CAD,140";
        Assert.Throws<FormatException>(() => TradeCsv.Parse(new StringReader(text)));
    }

    [Fact]
    public void Command_line_rejects_unknown_and_valueless_options()
    {
        Assert.Throws<UsageException>(() => CliArgs.Parse(new[] { "book", "--date" }));
        var cli = CliArgs.Parse(new[] { "book", "--dat", "2026-11-23", "--json" });
        Assert.True(cli.Json);
        Assert.Throws<UsageException>(() => cli.AllowOnly("date"));
    }
}
