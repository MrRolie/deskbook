using Deskbook.Core.Reporting;
using Deskbook.Cli;
using Deskbook.Core;
using Deskbook.Infrastructure.Valet;
using static Deskbook.Tests.Build;

namespace Deskbook.Tests;

/// <summary>
/// The book's arithmetic. Rates marked "BoC" are Bank of Canada daily rates (Valet, FX_RATES_DAILY), copied
/// unchanged; the expected numbers were worked out by hand from them.
/// </summary>
public class BookCalculatorTests
{
    private static readonly Trade BuyUsd = Trade("FICT-0001", "2026-11-03", "USD", 1_000_000m, "CAD", 1_399_000m);

    [Fact]
    public void A_new_trade_is_worth_its_legs_at_the_days_rate()
    {
        // BoC 2026-11-03: USD 1.3994. 1,000,000 x 1.3994 - 1,399,000 = 400.
        var book = BookCalculator.Compute(Rates("2026-11-03", ("USD", 1.3994m)), null, new[] { BuyUsd });

        Assert.Equal(400m, book.ValueCad);
        Assert.Equal(400m, book.NewTradesPnlCad);
        Assert.Equal(0m, book.RevaluationPnlCad);
        Assert.Equal(400m, book.TotalPnlCad);
        Assert.Equal(new[] { "FICT-0001" }, book.NewTradeIds);
    }

    [Fact]
    public void A_held_position_is_revalued_by_the_rate_change()
    {
        // BoC USD 1.3994 on 2026-11-03, 1.3961 on 2026-11-04: 1,000,000 x (1.3961 - 1.3994) = -3,300.
        var book = BookCalculator.Compute(
            Rates("2026-11-04", ("USD", 1.3961m)), Rates("2026-11-03", ("USD", 1.3994m)), new[] { BuyUsd });

        Assert.Equal(-3_300m, book.RevaluationPnlCad);
        Assert.Equal(0m, book.NewTradesPnlCad);
        Assert.Equal(400m, book.PreviousValueCad);
        Assert.Equal(-2_900m, book.ValueCad);
        Assert.Equal(-3_300m, book.TotalPnlCad);
        Assert.Empty(book.NewTradeIds);
    }

    [Fact]
    public void The_sample_book_for_2026_11_23_matches_the_hand_calculation()
    {
        var book = SampleBook("2026-11-23", "2026-11-20");

        // Positions after all eight fictional trades, valued at BoC 2026-11-23 rates:
        // -474,400 CAD + 300,000 x 1.7063 - 824,000 x 1.5672 + 100,000,000 x 0.009030 + 274,000 x 1.4369 = 42,827.80
        Assert.Equal(42_827.80m, book.ValueCad);
        // The same positions before FICT-0008, at BoC 2026-11-20 rates:
        // 100,100 + 300,000 x 1.7067 - 824,000 x 1.5727 + 100,000,000 x 0.009030 - 126,000 x 1.4325 = 38,710.20
        Assert.Equal(38_710.20m, book.PreviousValueCad);
        // FICT-0008 bought USD 400,000 for CAD 574,500: 400,000 x 1.4369 - 574,500 = 260.
        Assert.Equal(260m, book.NewTradesPnlCad);
        Assert.Equal(4_117.60m, book.TotalPnlCad);
    }

    [Theory]
    [InlineData("2026-11-04", "2026-11-03")]
    [InlineData("2026-11-12", "2026-11-10")]
    [InlineData("2026-11-23", "2026-11-20")]
    public void The_days_PnL_is_exactly_new_trades_plus_revaluation(string date, string previous)
    {
        var book = SampleBook(date, previous);
        Assert.Equal(book.TotalPnlCad, book.NewTradesPnlCad + book.RevaluationPnlCad);
        Assert.Equal(book.ValueCad, book.Positions.Sum(p => p.ValueCad));
    }

    [Fact]
    public void A_trade_dated_on_a_holiday_belongs_to_the_next_published_day()
    {
        // FICT-0004 is dated 2026-11-11, when the Bank published nothing.
        var book = SampleBook("2026-11-12", "2026-11-10");
        Assert.Equal(new[] { "FICT-0004" }, book.NewTradeIds);
    }

    [Fact]
    public void Trades_after_the_book_date_are_ignored()
    {
        var later = Trade("FICT-9999", "2026-11-04", "EUR", 1m, "CAD", 1m);
        var book = BookCalculator.Compute(Rates("2026-11-03", ("USD", 1.3994m)), null, new[] { BuyUsd, later });
        Assert.DoesNotContain(book.Positions, p => p.Currency.Code == "EUR");
    }

    [Fact]
    public void A_missing_rate_for_a_held_currency_refuses_the_book()
    {
        var refusal = Assert.Throws<BookRefusedException>(() =>
            BookCalculator.Compute(Rates("2026-11-03", ("EUR", 1.5661m)), null, new[] { BuyUsd }));
        Assert.Contains("no USD rate published for 2026-11-03", refusal.Reasons);
    }

    [Fact]
    public void A_missing_previous_rate_for_a_held_currency_refuses_the_book()
    {
        var refusal = Assert.Throws<BookRefusedException>(() => BookCalculator.Compute(
            Rates("2026-11-04", ("USD", 1.3961m)), Rates("2026-11-03", ("EUR", 1.5661m)), new[] { BuyUsd }));
        Assert.Contains("no USD rate published for 2026-11-03", refusal.Reasons);
    }

    [Fact]
    public void Trades_before_the_first_stored_rates_refuse_the_book()
    {
        Assert.Throws<BookRefusedException>(() =>
            BookCalculator.Compute(Rates("2026-11-04", ("USD", 1.3961m)), null, new[] { BuyUsd }));
    }

    [Fact]
    public void A_currency_that_is_flat_and_untraded_needs_no_rate()
    {
        // Bought and sold the same RUB amount earlier: no position, so no RUB rate is needed (the Bank publishes none).
        var trades = new[]
        {
            Trade("FICT-A", "2026-11-03", "RUB", 1_000m, "CAD", 17m),
            Trade("FICT-B", "2026-11-03", "CAD", 17m, "RUB", 1_000m),
        };
        var book = BookCalculator.Compute(Rates("2026-11-04", ("USD", 1.3961m)), Rates("2026-11-03", ("USD", 1.3994m)), trades);
        Assert.Equal(0m, book.ValueCad);
    }

    [Fact]
    public void The_same_inputs_give_the_same_book_twice_byte_for_byte()
    {
        var first = Output.ToJson(BookReport.ToJson(SampleBook("2026-11-23", "2026-11-20")));
        var second = Output.ToJson(BookReport.ToJson(SampleBook("2026-11-23", "2026-11-20")));
        Assert.Equal(first, second);
        Assert.Contains("\"fictional\": true", first);
    }

    private static Book SampleBook(string date, string previous)
    {
        var days = ValetParser.ParseDays(File.ReadAllText(Fixture("valet-fx-2026-11.json")));
        using var csv = new StreamReader(Sample("trades-fictional.csv"));
        var trades = TradeCsv.Parse(csv);
        return BookCalculator.Compute(days.Single(d => d.Date == D(date)), days.Single(d => d.Date == D(previous)), trades);
    }
}
