namespace Deskbook.Core.Bonds;

/// <summary>
/// A fixed-coupon bond paying half its annual coupon every six months, the last coupon with the principal on
/// <see cref="Maturity"/> (the Government of Canada pattern). Every bond in this project is invented and its
/// description says so.
/// </summary>
public sealed record Bond
{
    public string Id { get; }
    public string Description { get; }
    /// <summary>Annual coupon in percent of face, for example 3.25.</summary>
    public decimal CouponPercent { get; }
    public DateOnly Maturity { get; }

    public Bond(string id, string description, decimal couponPercent, DateOnly maturity)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A bond needs an id.");
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException($"Bond {id} needs a description.");
        if (couponPercent < 0 || couponPercent > 25) throw new ArgumentException($"Bond {id}: coupon {couponPercent}% is not plausible.");
        Id = id.Trim();
        Description = description.Trim();
        CouponPercent = couponPercent;
        Maturity = maturity;
    }

    /// <summary>The coupon dates, counted back in six-month steps from maturity, oldest first, down to the first after <paramref name="after"/>.</summary>
    public IReadOnlyList<DateOnly> CouponDatesAfter(DateOnly after)
    {
        var dates = new List<DateOnly>();
        for (var k = 0; ; k++)
        {
            var date = Maturity.AddMonths(-6 * k);
            if (date <= after) break;
            dates.Add(date);
        }
        dates.Reverse();
        return dates;
    }

    /// <summary>
    /// The coupon period holding <paramref name="date"/>: from the coupon on or before it to the next one. Both ends
    /// are counted back from maturity, so a bond maturing on the 31st keeps its month-end dates.
    /// </summary>
    public (DateOnly Start, DateOnly End) CouponPeriod(DateOnly date)
    {
        if (date >= Maturity) throw new ArgumentException($"Bond {Id} has matured.");
        var k = 1;
        while (Maturity.AddMonths(-6 * k) > date) k++;
        return (Maturity.AddMonths(-6 * k), Maturity.AddMonths(-6 * (k - 1)));
    }

    /// <summary>Every cash flow per 100 of face paid in (<paramref name="after"/>, <paramref name="upTo"/>]: coupons, and the principal at maturity.</summary>
    public decimal CashFlowsPer100(DateOnly after, DateOnly upTo)
    {
        var total = 0m;
        foreach (var date in CouponDatesAfter(after).Where(d => d <= upTo))
        {
            total += CouponPer100 + (date == Maturity ? 100m : 0m);
        }
        return total;
    }

    /// <summary>One coupon, per 100 of face.</summary>
    public decimal CouponPer100 => CouponPercent / 2m;
}

/// <summary>A purchase or sale of a bond by the (fictional) desk, at a clean price per 100 of face.</summary>
public sealed record BondTrade
{
    public string Id { get; }
    public DateOnly TradeDate { get; }
    public string BondId { get; }
    /// <summary>Face amount in CAD: positive for a purchase, negative for a sale.</summary>
    public decimal Face { get; }
    public decimal CleanPrice { get; }
    public string Counterparty { get; }

    public BondTrade(string id, DateOnly tradeDate, string bondId, decimal face, decimal cleanPrice, string counterparty)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A bond trade needs an id.");
        if (face == 0) throw new ArgumentException($"Bond trade {id}: the face amount cannot be zero.");
        if (cleanPrice <= 0 || cleanPrice > 300) throw new ArgumentException($"Bond trade {id}: price {cleanPrice} is not plausible.");
        if (string.IsNullOrWhiteSpace(counterparty)) throw new ArgumentException($"Bond trade {id} needs a counterparty.");
        Id = id.Trim();
        TradeDate = tradeDate;
        BondId = bondId.Trim();
        Face = face;
        CleanPrice = cleanPrice;
        Counterparty = counterparty.Trim();
    }
}
