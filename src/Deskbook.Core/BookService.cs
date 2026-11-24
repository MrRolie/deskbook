namespace Deskbook.Core;

/// <summary>
/// Builds the book for a date from stored data: checks the calendar, loads the two days of rates and the trades,
/// and hands them to <see cref="BookCalculator"/>.
/// </summary>
public sealed class BookService
{
    private readonly IBookSource _source;

    public BookService(IBookSource source)
    {
        _source = source;
    }

    /// <param name="date">The book date; null means the latest day with published rates.</param>
    public async Task<Book> BuildAsync(DateOnly? date, CancellationToken cancellationToken = default)
    {
        var calendar = await _source.LoadCalendarAsync(cancellationToken);
        var bookDate = date ?? calendar.LatestPublished
            ?? throw new BookRefusedException("no rates are stored yet; run 'deskbook rates fetch'");

        var previousDate = calendar.EnsureBookable(bookDate);
        var today = await _source.LoadRatesAsync(bookDate, cancellationToken);
        var previous = previousDate is null ? null : await _source.LoadRatesAsync(previousDate.Value, cancellationToken);
        var trades = await _source.LoadTradesAsync(bookDate, cancellationToken);

        return BookCalculator.Compute(today, previous, trades);
    }
}
