using Deskbook.Core;
using Deskbook.Infrastructure.Postgres;
using Deskbook.Infrastructure.Valet;
using static Deskbook.IntegrationTests.Files;

namespace Deskbook.IntegrationTests;

public class PostgresStoreTests : IClassFixture<TestDatabase>, IAsyncLifetime
{
    private readonly TestDatabase _db;
    private readonly PostgresStore _store;

    public PostgresStoreTests(TestDatabase db)
    {
        _db = db;
        _store = new PostgresStore(db.DataSource);
    }

    public async Task InitializeAsync()
    {
        await _store.InitializeAsync();
        await using var clear = _db.DataSource.CreateCommand("TRUNCATE trade, rate, rate_revision, rate_fetch");
        await clear.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_schema_can_be_applied_again()
    {
        await _store.InitializeAsync();
        Assert.Equal(0, await _db.CountAsync("trade"));
    }

    [Fact]
    public async Task Importing_the_same_trades_twice_changes_nothing()
    {
        var trades = new[] { Trade("FICT-1", "2026-11-03", "USD", 1_000_000m, "CAD", 1_399_000m) };
        Assert.Equal(new ImportOutcome(1, 0), await _store.AddTradesAsync(trades));
        Assert.Equal(new ImportOutcome(0, 1), await _store.AddTradesAsync(trades));
        Assert.Equal(1, await _db.CountAsync("trade"));
    }

    [Fact]
    public async Task A_stored_trade_is_never_rewritten_and_a_conflict_imports_nothing()
    {
        await _store.AddTradesAsync(new[] { Trade("FICT-1", "2026-11-03", "USD", 1_000_000m, "CAD", 1_399_000m) });

        var batch = new[]
        {
            Trade("FICT-2", "2026-11-04", "EUR", 1_000m, "CAD", 1_570m),
            Trade("FICT-1", "2026-11-03", "USD", 2_000_000m, "CAD", 2_798_000m),
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.AddTradesAsync(batch));

        var stored = await _store.ListTradesAsync();
        Assert.Single(stored);
        Assert.Equal(1_000_000m, stored[0].Bought.Amount);
    }

    [Fact]
    public async Task Rates_keep_their_published_precision_in_the_database()
    {
        await _store.StoreFetchAsync(Observations(Day("2026-11-03", ("JPY", 0.008830m), ("USD", 1.3994m))));
        var rates = await _store.LoadRatesAsync(D("2026-11-03"));

        Assert.True(rates.TryGetCadPerUnit(new Currency("JPY"), out var jpy));
        Assert.Equal("0.008830", jpy.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_changed_rate_is_updated_and_its_old_value_kept()
    {
        await _store.StoreFetchAsync(Observations(Day("2026-11-03", ("USD", 1.3994m), ("EUR", 1.5661m))));
        var outcome = await _store.StoreFetchAsync(Observations(Day("2026-11-03", ("USD", 1.3990m), ("EUR", 1.5661m))));

        Assert.Equal(0, outcome.RatesAdded);
        Assert.Equal(1, outcome.RatesUnchanged);
        var revision = Assert.Single(outcome.Revisions);
        Assert.Equal((1.3994m, 1.3990m), (revision.OldCadPerUnit, revision.NewCadPerUnit));

        Assert.Single(await _store.ListRevisionsAsync());
        var rates = await _store.LoadRatesAsync(D("2026-11-03"));
        Assert.True(rates.TryGetCadPerUnit(new Currency("USD"), out var usd));
        Assert.Equal(1.3990m, usd);
    }

    [Fact]
    public async Task A_fetch_vouches_only_up_to_its_last_day_with_rates()
    {
        // Asked up to 2026-11-25, but the answer's last day is 2026-11-23: the 24th is unknown, not a holiday.
        var observations = new ValetObservations("FX_RATES_DAILY", D("2026-11-20"), D("2026-11-25"), "test", DateTimeOffset.UnixEpoch,
            new[] { Day("2026-11-20", ("USD", 1.4325m)), Day("2026-11-23", ("USD", 1.4369m)) });
        await _store.StoreFetchAsync(observations);

        var calendar = await _store.LoadCalendarAsync();
        Assert.True(calendar.IsFetched(D("2026-11-22")));
        Assert.False(calendar.IsFetched(D("2026-11-24")));
        Assert.Equal(D("2026-11-20"), calendar.EnsureBookable(D("2026-11-23")));
    }

    private static RateSet Day(string date, params (string Currency, decimal Rate)[] rates) =>
        new(D(date), rates.ToDictionary(r => new Currency(r.Currency), r => r.Rate));

    private static ValetObservations Observations(params RateSet[] days) =>
        new("FX_RATES_DAILY", days[0].Date, days[^1].Date, "test", DateTimeOffset.UnixEpoch, days);
}
