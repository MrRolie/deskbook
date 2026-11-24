using Deskbook.Core;
using Deskbook.Core.Bonds;
using Deskbook.Infrastructure.Valet;
using static Deskbook.Tests.Build;

namespace Deskbook.Tests;

/// <summary>
/// Bond pricing and the bond book. Expected prices come from tools/reference/bond_reference.py, a separate Python
/// implementation of the same street convention (not from this C#), printed to 8 decimals; the book's expected
/// numbers come from the same script.
/// </summary>
public class BondTests
{
    private static readonly Bond B2031 = new("FICT-CAN-2031-06", "Fictional federal 3.25% bond due 2031-06-01", 3.25m, D("2031-06-01"));

    private static void Near(decimal expected, decimal actual, decimal tolerance = 0.0000001m) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void A_bond_whose_coupon_equals_its_yield_prices_at_par_on_a_coupon_date()
    {
        var bond = new Bond("P", "Fictional par bond", 4.0m, D("2031-06-01"));
        var price = BondPricer.Price(bond, D("2026-12-01"), 4.0m);
        Near(100m, price.FullPrice);
        Assert.Equal(0m, price.Accrued);
    }

    [Fact]
    public void Price_accrued_and_DV01_match_the_reference_implementation()
    {
        // Reference: 3.25% 2031-06-01 on 2026-11-23 at 3.52%: full 100.43373770, accrued 1.55396175 (1.625 x 175/183),
        // clean 98.87977596, DV01 0.04119964 per 100.
        var price = BondPricer.Price(B2031, D("2026-11-23"), 3.52m);
        Near(100.43373770m, price.FullPrice);
        Near(1.55396175m, price.Accrued);
        Near(98.87977596m, price.CleanPrice);
        Near(0.04119964m, price.Dv01Per100);
    }

    [Fact]
    public void A_second_bond_matches_the_reference_too()
    {
        var bond = new Bond("B", "Fictional federal 2.75% bond due 2029-09-01", 2.75m, D("2029-09-01"));
        Near(98.89505499m, BondPricer.Price(bond, D("2026-11-20"), 3.40m).FullPrice);
    }

    [Fact]
    public void Coupon_periods_count_back_from_a_month_end_maturity()
    {
        var bond = new Bond("M", "Fictional month-end bond", 3m, D("2031-08-31"));
        Assert.Equal((D("2026-02-28"), D("2026-08-31")), bond.CouponPeriod(D("2026-03-15")));
    }

    [Fact]
    public void The_last_cash_flow_includes_the_principal()
    {
        Assert.Equal(1.625m + 100m, B2031.CashFlowsPer100(D("2031-05-01"), D("2031-06-01")));
        Assert.Equal(1.625m, B2031.CashFlowsPer100(D("2026-11-01"), D("2026-12-01")));
    }

    [Fact]
    public void The_curve_interpolates_in_years_and_refuses_outside_two_to_ten()
    {
        var curve = new YieldCurve(D("2026-11-20"), new Dictionary<int, decimal> { [2] = 3.19m, [3] = 3.29m, [5] = 3.41m, [7] = 3.50m, [10] = 3.69m });

        Assert.True(curve.TryYieldFor(D("2031-06-01"), out var y, out _));
        var years = (D("2031-06-01").DayNumber - D("2026-11-20").DayNumber) / 365.25m;
        Assert.Equal(3.29m + (3.41m - 3.29m) * (years - 3) / 2, y);

        Assert.False(curve.TryYieldFor(D("2028-01-01"), out _, out var shortReason));
        Assert.Contains("outside the benchmark curve", shortReason);
        Assert.False(curve.TryYieldFor(D("2040-01-01"), out _, out _));
    }

    [Fact]
    public void The_sample_bond_book_matches_the_reference_on_two_days()
    {
        var book = SampleBook("2026-11-20", "2026-11-19");
        Assert.Equal(72_035.43m, Math.Round(book.ValueCad, 2));
        Assert.Equal(-9_855.35m, Math.Round(book.PreviousValueCad, 2));
        Assert.Equal(81_890.77m, Math.Round(book.TotalPnlCad, 2));
        Assert.Equal(5_609.47m, Math.Round(book.Dv01Cad, 2));

        var earlier = SampleBook("2026-11-12", "2026-11-10");
        Assert.Equal(-23_107.86m, Math.Round(earlier.ValueCad, 2));
        Assert.Equal(-35_158.19m, Math.Round(earlier.TotalPnlCad, 2));
    }

    [Theory]
    [InlineData("2026-11-05", "2026-11-04")]
    [InlineData("2026-11-12", "2026-11-10")]
    [InlineData("2026-11-20", "2026-11-19")]
    public void The_bond_PnL_is_held_plus_new_trades(string date, string previous)
    {
        var book = SampleBook(date, previous);
        Near(book.TotalPnlCad, book.HeldPnlCad + book.NewTradesPnlCad, 0.000001m);
    }

    [Fact]
    public void Across_a_coupon_date_the_coupon_received_offsets_the_price_drop()
    {
        // Held over 2026-12-01, a coupon date, with the yield unchanged: the full price falls by about the coupon
        // (1.625 per 100) and the coupon is received, so the held P&L is only a day of carry, and the parts still add up.
        var trade = new BondTrade("T", D("2026-11-02"), B2031.Id, 1_000_000m, 99m, "Test (fictional)");
        var points = new Dictionary<int, decimal> { [3] = 3.4m, [5] = 3.5m };
        var book = BondBookCalculator.Compute(new YieldCurve(D("2026-12-01"), points), new YieldCurve(D("2026-11-30"), points),
            new Dictionary<string, Bond> { [B2031.Id] = B2031 }, new[] { trade });

        Near(book.TotalPnlCad, book.HeldPnlCad + book.NewTradesPnlCad, 0.000001m);
        Assert.InRange(book.HeldPnlCad, 0m, 200m);
    }

    [Fact]
    public void A_matured_bond_leaves_only_its_cash()
    {
        var bond = new Bond("OLD", "Fictional matured bond", 4m, D("2026-12-01"));
        var trade = new BondTrade("T", D("2026-06-01"), "OLD", 1_000_000m, 100m, "Test (fictional)");
        var curve = new YieldCurve(D("2026-12-03"), new Dictionary<int, decimal> { [2] = 3m, [3] = 3m });
        var before = new YieldCurve(D("2026-12-02"), new Dictionary<int, decimal> { [2] = 3m, [3] = 3m });

        var book = BondBookCalculator.Compute(curve, before, new Dictionary<string, Bond> { ["OLD"] = bond }, new[] { trade });

        // Paid 1,000,000 on a coupon date (no accrued); received one 2% coupon plus the principal: 20,000 left.
        Assert.Equal(0m, book.MarketValueCad);
        Assert.Equal(20_000m, book.CashCad);
        Assert.Equal(0m, book.TotalPnlCad);
    }

    [Fact]
    public void A_trade_in_an_undefined_bond_refuses_the_book()
    {
        var curve = new YieldCurve(D("2026-11-20"), new Dictionary<int, decimal> { [5] = 3.41m });
        var trade = new BondTrade("T", D("2026-11-20"), "NOPE", 100m, 99m, "Test (fictional)");
        Assert.Throws<BookRefusedException>(() => BondBookCalculator.Compute(curve, null, new Dictionary<string, Bond>(), new[] { trade }));
    }

    private static BondBook SampleBook(string date, string previous)
    {
        var curves = ValetParser.ParseYieldDays(File.ReadAllText(Fixture("valet-bond-yields-2026-11.json")));
        using var bondFile = new StreamReader(Sample("bonds-fictional.csv"));
        using var tradeFile = new StreamReader(Sample("bond-trades-fictional.csv"));
        var bonds = BondCsv.ParseBonds(bondFile).ToDictionary(b => b.Id);
        return BondBookCalculator.Compute(curves.Single(c => c.Date == D(date)), curves.Single(c => c.Date == D(previous)),
            bonds, BondCsv.ParseTrades(tradeFile));
    }
}
