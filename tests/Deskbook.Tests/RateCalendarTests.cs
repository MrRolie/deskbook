using Deskbook.Core;
using static Deskbook.Tests.Build;

namespace Deskbook.Tests;

public class RateCalendarTests
{
    // Published days around the 2026-11-11 holiday, as the Bank of Canada published them.
    private static readonly DateOnly[] Published = { D("2026-11-09"), D("2026-11-10"), D("2026-11-12"), D("2026-11-13") };

    [Fact]
    public void The_previous_published_day_skips_a_holiday()
    {
        var calendar = new RateCalendar(Published, new[] { new DateRange(D("2026-11-09"), D("2026-11-13")) });
        Assert.Equal(D("2026-11-10"), calendar.EnsureBookable(D("2026-11-12")));
    }

    [Fact]
    public void A_fetched_day_with_no_rates_is_refused_as_unpublished()
    {
        var calendar = new RateCalendar(Published, new[] { new DateRange(D("2026-11-09"), D("2026-11-13")) });
        var refusal = Assert.Throws<BookRefusedException>(() => calendar.EnsureBookable(D("2026-11-11")));
        Assert.Contains("published no rates for 2026-11-11", refusal.Message);
    }

    [Fact]
    public void A_day_never_fetched_is_refused_as_unknown_not_as_a_holiday()
    {
        var calendar = new RateCalendar(Published, new[] { new DateRange(D("2026-11-09"), D("2026-11-13")) });
        var refusal = Assert.Throws<BookRefusedException>(() => calendar.EnsureBookable(D("2026-11-16")));
        Assert.Contains("never fetched", refusal.Message);
    }

    [Fact]
    public void A_hole_in_what_was_fetched_since_the_previous_day_is_refused()
    {
        // Two separate fetches leave 2026-11-11 unasked: it could have been a business day.
        var calendar = new RateCalendar(Published, new[]
        {
            new DateRange(D("2026-11-09"), D("2026-11-10")),
            new DateRange(D("2026-11-12"), D("2026-11-13")),
        });
        var refusal = Assert.Throws<BookRefusedException>(() => calendar.EnsureBookable(D("2026-11-12")));
        Assert.Contains("between 2026-11-10 and 2026-11-12 were never fetched", refusal.Message);
    }

    [Fact]
    public void The_first_published_day_has_no_previous_day()
    {
        var calendar = new RateCalendar(Published, new[] { new DateRange(D("2026-11-09"), D("2026-11-13")) });
        Assert.Null(calendar.EnsureBookable(D("2026-11-09")));
        Assert.Equal(D("2026-11-13"), calendar.LatestPublished);
    }
}
