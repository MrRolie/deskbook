namespace Deskbook.Core;

/// <summary>What the book needs to read from storage. PostgreSQL implements it; tests may use a fake.</summary>
public interface IBookSource
{
    Task<RateCalendar> LoadCalendarAsync(CancellationToken cancellationToken = default);
    Task<RateSet> LoadRatesAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Trade>> LoadTradesAsync(DateOnly upTo, CancellationToken cancellationToken = default);
}
