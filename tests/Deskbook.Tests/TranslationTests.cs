using Deskbook.Core;
using Deskbook.Infrastructure.Valet;
using static Deskbook.Tests.Build;

namespace Deskbook.Tests;

public class TranslationTests
{
    private static readonly Currency Usd = new("USD");

    private static (Book Book, RateSet Today, RateSet Previous) SampleBook()
    {
        var days = ValetParser.ParseDays(File.ReadAllText(Fixture("valet-fx-2026-11.json")));
        using var csv = new StreamReader(Sample("trades-fictional.csv"));
        var today = days.Single(d => d.Date == D("2026-11-23"));
        var previous = days.Single(d => d.Date == D("2026-11-20"));
        return (BookCalculator.Compute(today, previous, TradeCsv.Parse(csv)), today, previous);
    }

    private static Translation Translate(Book book, RateSet today, RateSet? previous, Currency currency) =>
        Translation.Translate(currency, today, previous, book.ValueCad, book.PreviousValueCad,
            new[] { ("new_trades", book.NewTradesPnlCad), ("revaluation", book.RevaluationPnlCad) });

    [Fact]
    public void The_sample_book_in_USD_matches_the_hand_calculation()
    {
        var (book, today, previous) = SampleBook();
        var usd = Translate(book, today, previous, Usd);

        // 42,827.80 / 1.4369 = 29,805.69; 38,710.20 / 1.4325 = 27,022.83;
        // translation = 38,710.20 x (1 / 1.4369 - 1 / 1.4325) = -82.75.
        Assert.Equal(29_805.69m, Math.Round(usd.Value, 2));
        Assert.Equal(27_022.83m, Math.Round(usd.PreviousValue, 2));
        Assert.Equal(-82.75m, Math.Round(usd.TranslationEffect, 2));
        Assert.Equal(2_782.87m, Math.Round(usd.TotalPnl, 2));
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("JPY")]
    public void The_parts_and_the_translation_effect_add_up_to_the_total(string code)
    {
        var (book, today, previous) = SampleBook();
        var t = Translate(book, today, previous, new Currency(code));
        var sum = t.Parts.Sum(p => p.Amount) + t.TranslationEffect;
        Assert.True(Math.Abs(t.TotalPnl - sum) < 0.000000001m, $"{t.TotalPnl} vs {sum}");
    }

    [Fact]
    public void A_currency_without_a_published_rate_refuses()
    {
        var (book, today, previous) = SampleBook();
        Assert.Throws<BookRefusedException>(() => Translate(book, today, previous, new Currency("RUB")));
    }

    [Fact]
    public void CAD_is_not_a_second_currency()
    {
        var (book, today, previous) = SampleBook();
        Assert.Throws<ArgumentException>(() => Translate(book, today, previous, Currency.Cad));
    }
}
