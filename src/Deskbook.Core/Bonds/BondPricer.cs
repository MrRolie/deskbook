namespace Deskbook.Core.Bonds;

/// <summary>A bond's price on one date, per 100 of face.</summary>
public sealed record BondPrice(decimal YieldPercent, decimal FullPrice, decimal Accrued, decimal CleanPrice, decimal Dv01Per100);

/// <summary>
/// Prices a bond from a yield, by the textbook street convention for semi-annual bonds:
/// full price = sum over remaining cash flows of CF / (1 + y/2)^(k - 1 + w), where k counts the coupons from the next
/// one and w is the fraction of the current coupon period still to run (actual days / actual days in the period).
/// Accrued interest uses the same fraction (actual/actual within the period); clean price = full − accrued.
/// DV01 is the price change for one basis point, by repricing 1 bp lower and 1 bp higher.
/// This is a model valuation from benchmark yields, not a market quote.
/// </summary>
public static class BondPricer
{
    public static BondPrice Price(Bond bond, DateOnly date, decimal yieldPercent)
    {
        if (date >= bond.Maturity) throw new ArgumentException($"Bond {bond.Id} has matured.");

        var full = FullPrice(bond, date, yieldPercent);
        var accrued = Accrued(bond, date);
        var dv01 = (FullPrice(bond, date, yieldPercent - 0.01m) - FullPrice(bond, date, yieldPercent + 0.01m)) / 2m;
        return new BondPrice(yieldPercent, full, accrued, full - accrued, dv01);
    }

    /// <summary>Accrued interest per 100 on <paramref name="date"/>: the coupon times the share of the period gone by.</summary>
    public static decimal Accrued(Bond bond, DateOnly date)
    {
        var (start, end) = bond.CouponPeriod(date);
        return bond.CouponPer100 * (date.DayNumber - start.DayNumber) / (end.DayNumber - start.DayNumber);
    }

    private static decimal FullPrice(Bond bond, DateOnly date, decimal yieldPercent)
    {
        var (start, next) = bond.CouponPeriod(date);
        // decimal has no fractional power, so discount factors are computed in double; each present value is
        // rounded to 10 decimals per 100 of face before it enters a decimal sum.
        var w = (double)(next.DayNumber - date.DayNumber) / (next.DayNumber - start.DayNumber);
        var perPeriod = 1.0 + (double)yieldPercent / 200.0;

        var coupons = bond.CouponDatesAfter(date);
        decimal total = 0;
        for (var k = 0; k < coupons.Count; k++)
        {
            var cashFlow = bond.CouponPer100 + (k == coupons.Count - 1 ? 100m : 0m);
            var discount = Math.Pow(perPeriod, -(k + w));
            total += Math.Round(cashFlow * (decimal)discount, 10);
        }
        return total;
    }
}
