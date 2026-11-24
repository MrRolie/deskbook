using Deskbook.Core;
using Deskbook.Infrastructure.Valet;
using Npgsql;

namespace Deskbook.Infrastructure.Postgres;

/// <summary>What one stored fetch changed.</summary>
public sealed record FetchOutcome(long FetchId, int DaysWithRates, int RatesAdded, int RatesUnchanged, IReadOnlyList<RateRevision> Revisions)
{
    /// <summary>For a benchmark-yield fetch: how many stored yields changed (kept in yield_revision).</summary>
    public int YieldRevisions { get; init; }
}

/// <summary>A stored rate that a later fetch changed. The old value is kept in the rate_revision table.</summary>
public sealed record RateRevision(DateOnly Date, Currency Currency, decimal OldCadPerUnit, decimal NewCadPerUnit);

/// <summary>What one trade import changed.</summary>
public sealed record ImportOutcome(int Added, int AlreadyPresent);

/// <summary>
/// All reading and writing of the database. Plain SQL through Npgsql, with parameters, inside transactions where
/// several rows must change together.
/// </summary>
public sealed class PostgresStore : IBookSource
{
    private readonly NpgsqlDataSource _db;

    public PostgresStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>Creates the tables if they do not exist. Safe to run again.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = typeof(PostgresStore).Assembly.GetManifestResourceStream("Deskbook.Infrastructure.Postgres.schema.sql")
            ?? throw new InvalidOperationException("schema.sql is not embedded in the assembly");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(cancellationToken);

        await using var command = _db.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ---- Trades ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Adds trades in one transaction. A trade whose id is already stored with identical fields is counted as
    /// already present, so importing the same file twice changes nothing. A stored id with different fields
    /// aborts the whole import: a trade is never silently rewritten.
    /// </summary>
    public async Task<ImportOutcome> AddTradesAsync(IEnumerable<Trade> trades, CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        int added = 0, present = 0;

        foreach (var trade in trades)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO trade (id, trade_date, counterparty, bought_currency, bought_amount, sold_currency, sold_amount)
                VALUES ($1, $2, $3, $4, $5, $6, $7)
                ON CONFLICT (id) DO NOTHING
                """, connection, transaction);
            insert.Parameters.Add(new() { Value = trade.Id });
            insert.Parameters.Add(new() { Value = trade.TradeDate });
            insert.Parameters.Add(new() { Value = trade.Counterparty });
            insert.Parameters.Add(new() { Value = trade.Bought.Currency.Code });
            insert.Parameters.Add(new() { Value = trade.Bought.Amount });
            insert.Parameters.Add(new() { Value = trade.Sold.Currency.Code });
            insert.Parameters.Add(new() { Value = trade.Sold.Amount });

            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                added++;
                continue;
            }

            var stored = await ReadTradeAsync(connection, transaction, trade.Id, cancellationToken);
            if (stored != trade)
            {
                throw new InvalidOperationException(
                    $"trade '{trade.Id}' is already stored with different fields; nothing was imported");
            }
            present++;
        }

        await transaction.CommitAsync(cancellationToken);
        return new ImportOutcome(added, present);
    }

    public async Task<IReadOnlyList<Trade>> LoadTradesAsync(DateOnly upTo, CancellationToken cancellationToken = default)
    {
        await using var command = _db.CreateCommand(
            $"SELECT {TradeColumns} FROM trade WHERE trade_date <= $1 ORDER BY trade_date, id");
        command.Parameters.Add(new() { Value = upTo });
        return await ReadTradesAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<Trade>> ListTradesAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _db.CreateCommand($"SELECT {TradeColumns} FROM trade ORDER BY trade_date, id");
        return await ReadTradesAsync(command, cancellationToken);
    }

    private const string TradeColumns =
        "id, trade_date, counterparty, bought_currency, bought_amount, sold_currency, sold_amount";

    private static async Task<Trade?> ReadTradeAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT {TradeColumns} FROM trade WHERE id = $1", connection, transaction);
        command.Parameters.Add(new() { Value = id });
        var trades = await ReadTradesAsync(command, cancellationToken);
        return trades.Count == 0 ? null : trades[0];
    }

    private static async Task<IReadOnlyList<Trade>> ReadTradesAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var trades = new List<Trade>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            trades.Add(new Trade(
                reader.GetString(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetString(2),
                new Leg(new Currency(reader.GetString(3)), reader.GetDecimal(4)),
                new Leg(new Currency(reader.GetString(5)), reader.GetDecimal(6))));
        }
        return trades;
    }

    // ---- Rates -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Stores one Valet answer in one transaction: a rate_fetch row, each new rate, and for each stored rate whose
    /// value changed, a rate_revision row keeping the old value.
    /// </summary>
    public async Task<FetchOutcome> StoreFetchAsync(ValetObservations observations, CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long fetchId;
        await using (var insertFetch = new NpgsqlCommand(
            """
            INSERT INTO rate_fetch (series_group, requested_from, requested_to, covered_to, fetched_at, source_url)
            VALUES ($1, $2, $3, $4, $5, $6) RETURNING id
            """, connection, transaction))
        {
            insertFetch.Parameters.Add(new() { Value = observations.SeriesGroup });
            insertFetch.Parameters.Add(new() { Value = observations.RequestedFrom });
            insertFetch.Parameters.Add(new() { Value = observations.RequestedTo });
            insertFetch.Parameters.Add(new() { Value = observations.CoveredTo.HasValue ? observations.CoveredTo.Value : DBNull.Value });
            insertFetch.Parameters.Add(new() { Value = observations.FetchedAt });
            insertFetch.Parameters.Add(new() { Value = observations.SourceUrl });
            fetchId = (long)(await insertFetch.ExecuteScalarAsync(cancellationToken))!;
        }

        int added = 0, unchanged = 0;
        var revisions = new List<RateRevision>();
        foreach (var day in observations.Days)
        {
            var stored = await ReadRatesAsync(connection, transaction, day.Date, cancellationToken);
            foreach (var (currency, rate) in day.All())
            {
                if (!stored.TryGetValue(currency, out var old))
                {
                    await ExecuteAsync(connection, transaction,
                        "INSERT INTO rate (rate_date, currency, cad_per_unit, fetch_id) VALUES ($1, $2, $3, $4)",
                        cancellationToken, day.Date, currency.Code, rate, fetchId);
                    added++;
                }
                else if (old == rate)
                {
                    unchanged++;
                }
                else
                {
                    await ExecuteAsync(connection, transaction,
                        """
                        INSERT INTO rate_revision (rate_date, currency, old_cad_per_unit, new_cad_per_unit, fetch_id)
                        VALUES ($1, $2, $3, $4, $5)
                        """, cancellationToken, day.Date, currency.Code, old, rate, fetchId);
                    await ExecuteAsync(connection, transaction,
                        "UPDATE rate SET cad_per_unit = $3, fetch_id = $4 WHERE rate_date = $1 AND currency = $2",
                        cancellationToken, day.Date, currency.Code, rate, fetchId);
                    revisions.Add(new RateRevision(day.Date, currency, old, rate));
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new FetchOutcome(fetchId, observations.Days.Count, added, unchanged, revisions);
    }

    public async Task<RateSet> LoadRatesAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);
        var rates = await ReadRatesAsync(connection, null, date, cancellationToken);
        return new RateSet(date, rates);
    }

    public async Task<RateCalendar> LoadCalendarAsync(CancellationToken cancellationToken = default)
    {
        var published = new List<DateOnly>();
        await using (var command = _db.CreateCommand("SELECT DISTINCT rate_date FROM rate"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) published.Add(reader.GetFieldValue<DateOnly>(0));
        }

        var fetched = new List<DateRange>();
        await using (var command = _db.CreateCommand(
            "SELECT requested_from, covered_to FROM rate_fetch WHERE covered_to IS NOT NULL AND series_group = 'FX_RATES_DAILY'"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                fetched.Add(new DateRange(reader.GetFieldValue<DateOnly>(0), reader.GetFieldValue<DateOnly>(1)));
            }
        }

        return new RateCalendar(published, fetched);
    }

    public async Task<IReadOnlyList<RateRevision>> ListRevisionsAsync(CancellationToken cancellationToken = default)
    {
        var revisions = new List<RateRevision>();
        await using var command = _db.CreateCommand(
            "SELECT rate_date, currency, old_cad_per_unit, new_cad_per_unit FROM rate_revision ORDER BY id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            revisions.Add(new RateRevision(
                reader.GetFieldValue<DateOnly>(0), new Currency(reader.GetString(1)), reader.GetDecimal(2), reader.GetDecimal(3)));
        }
        return revisions;
    }

    private static async Task<Dictionary<Currency, decimal>> ReadRatesAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, DateOnly date, CancellationToken cancellationToken)
    {
        var rates = new Dictionary<Currency, decimal>();
        await using var command = new NpgsqlCommand(
            "SELECT currency, cad_per_unit FROM rate WHERE rate_date = $1", connection, transaction);
        command.Parameters.Add(new() { Value = date });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rates[new Currency(reader.GetString(0))] = reader.GetDecimal(1);
        }
        return rates;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken,
        params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values) command.Parameters.Add(new() { Value = value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
