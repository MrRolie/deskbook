using Deskbook.Core;
using static Deskbook.Tests.Build;

namespace Deskbook.Tests;

public class DomainTests
{
    [Theory]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("U1D")]
    [InlineData("")]
    public void Currency_rejects_anything_but_three_upper_case_letters(string code)
    {
        Assert.Throws<ArgumentException>(() => new Currency(code));
    }

    [Fact]
    public void Currencies_with_the_same_code_are_equal()
    {
        Assert.Equal(new Currency("USD"), new Currency("USD"));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, -1)]
    public void Trade_rejects_amounts_that_are_not_positive(decimal bought, decimal sold)
    {
        Assert.Throws<ArgumentException>(() => Trade("T", "2026-11-03", "USD", bought, "CAD", sold));
    }

    [Fact]
    public void Trade_rejects_the_same_currency_on_both_legs()
    {
        Assert.Throws<ArgumentException>(() => Trade("T", "2026-11-03", "USD", 1, "USD", 1));
    }

    [Fact]
    public void Trade_signed_legs_add_the_bought_amount_and_subtract_the_sold_amount()
    {
        var legs = Trade("T", "2026-11-03", "USD", 1_000_000m, "CAD", 1_399_000m).SignedLegs().ToList();
        Assert.Equal((new Currency("USD"), 1_000_000m), legs[0]);
        Assert.Equal((Currency.Cad, -1_399_000m), legs[1]);
    }

    [Fact]
    public void RateSet_values_CAD_at_one_and_never_stores_it()
    {
        var rates = Rates("2026-11-03", ("USD", 1.3994m));
        Assert.True(rates.TryGetCadPerUnit(Currency.Cad, out var cad));
        Assert.Equal(1m, cad);
        Assert.Throws<ArgumentException>(() => Rates("2026-11-03", ("CAD", 1m)));
    }

    [Fact]
    public void RateSet_rejects_a_rate_that_is_not_positive()
    {
        Assert.Throws<ArgumentException>(() => Rates("2026-11-03", ("USD", 0m)));
    }
}
