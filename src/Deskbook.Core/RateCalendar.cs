namespace Deskbook.Core;

/// <summary>An inclusive range of calendar dates, such as the range one rate fetch asked for.</summary>
public sealed record DateRange(DateOnly From, DateOnly To)
{
    public bool Contains(DateOnly date) => date >= From && date <= To;
}

/// <summary>
/// What the stored rates cover: which days have published rates, and which days were asked for at all.
/// The difference matters. A day that was fetched but has no rates is a day the Bank published nothing
/// (a weekend or a holiday such as 2026-11-11). A day never fetched is simply unknown, and a book must not
/// treat it as a holiday.
/// </summary>
public sealed class RateCalendar
{
    private readonly SortedSet<DateOnly> _published;
    private readonly IReadOnlyList<DateRange> _fetched;

    public RateCalendar(IEnumerable<DateOnly> publishedDates, IEnumerable<DateRange> fetchedRanges)
    {
        _published = new SortedSet<DateOnly>(publishedDates);
        _fetched = fetchedRanges.ToList();
    }

    /// <summary>Every day with published rates, oldest first.</summary>
    public IReadOnlyCollection<DateOnly> PublishedDates => _published;

    public DateOnly? LatestPublished => _published.Count == 0 ? null : _published.Max;

    public bool IsFetched(DateOnly date) => _fetched.Any(range => range.Contains(date));

    public DateOnly? PreviousPublished(DateOnly date)
    {
        var earlier = _published.GetViewBetween(DateOnly.MinValue, date.AddDays(-1));
        return earlier.Count == 0 ? null : earlier.Max;
    }

    /// <summary>
    /// Checks that a book can be produced for <paramref name="date"/> and returns the previous published day
    /// (null when there is none). Throws <see cref="BookRefusedException"/> when the stored rates cannot support it.
    /// </summary>
    public DateOnly? EnsureBookable(DateOnly date)
    {
        if (!IsFetched(date))
        {
            throw new BookRefusedException($"rates for {date:yyyy-MM-dd} were never fetched; run 'deskbook rates fetch' (or 'yields fetch' for bonds)");
        }
        if (!_published.Contains(date))
        {
            throw new BookRefusedException(
                $"the Bank of Canada published no rates for {date:yyyy-MM-dd} (a weekend, a holiday, or not yet published)");
        }

        var previous = PreviousPublished(date);
        if (previous is null) return null;

        var unfetched = new List<DateOnly>();
        for (var day = previous.Value.AddDays(1); day < date; day = day.AddDays(1))
        {
            if (!IsFetched(day)) unfetched.Add(day);
        }
        if (unfetched.Count > 0)
        {
            throw new BookRefusedException(
                $"rates between {previous:yyyy-MM-dd} and {date:yyyy-MM-dd} were never fetched " +
                $"({unfetched.Count} day(s) from {unfetched[0]:yyyy-MM-dd}), so the previous published day is unknown");
        }
        return previous;
    }
}
