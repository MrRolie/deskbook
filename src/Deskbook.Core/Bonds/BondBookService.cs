namespace Deskbook.Core.Bonds;

/// <summary>What the bond book needs to read from storage.</summary>
public interface IBondBookSource
{
    Task<RateCalendar> LoadYieldCalendarAsync(CancellationToken cancellationToken = default);
    Task<YieldCurve> LoadCurveAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, Bond>> LoadBondsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BondTrade>> LoadBondTradesAsync(DateOnly upTo, CancellationToken cancellationToken = default);
}

/// <summary>Builds the bond book for a date from stored data, with the same calendar rules as the FX book.</summary>
public sealed class BondBookService
{
    private readonly IBondBookSource _source;

    public BondBookService(IBondBookSource source)
    {
        _source = source;
    }

    public async Task<BondBook> BuildAsync(DateOnly? date, CancellationToken cancellationToken = default)
    {
        var calendar = await _source.LoadYieldCalendarAsync(cancellationToken);
        var bookDate = date ?? calendar.LatestPublished
            ?? throw new BookRefusedException("no yields are stored yet; run 'deskbook yields fetch'");
        var previousDate = calendar.EnsureBookable(bookDate);

        var today = await _source.LoadCurveAsync(bookDate, cancellationToken);
        var previous = previousDate is null ? null : await _source.LoadCurveAsync(previousDate.Value, cancellationToken);
        return BondBookCalculator.Compute(today, previous,
            await _source.LoadBondsAsync(cancellationToken), await _source.LoadBondTradesAsync(bookDate, cancellationToken));
    }
}
