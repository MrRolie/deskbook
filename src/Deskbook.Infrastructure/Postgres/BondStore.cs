using Deskbook.Core;
using Deskbook.Core.Bonds;
using Deskbook.Infrastructure.Valet;
using Npgsql;

namespace Deskbook.Infrastructure.Postgres;

/// <summary>Reads and writes the bond book: bonds, bond trades, and benchmark yields.</summary>
public sealed class BondStore : IBondBookSource
{
    private readonly NpgsqlDataSource _db;

    public BondStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>Adds bonds; an id already stored with identical fields is skipped, with different fields aborts all.</summary>
    public Task<ImportOutcome> AddBondsAsync(IEnumerable<Bond> bonds, CancellationToken cancellationToken = default) =>
        InsertAllAsync(bonds, b => b.Id,
            "INSERT INTO bond (id, description, coupon_percent, maturity) VALUES ($1, $2, $3, $4) ON CONFLICT (id) DO NOTHING",
            b => new object[] { b.Id, b.Description, b.CouponPercent, b.Maturity },
            async (connection, transaction, id) => (await LoadBondsAsync(connection, transaction, id)).GetValueOrDefault(id),
            cancellationToken);

    /// <summary>Adds bond trades, with the same rules; a trade in a bond not defined is refused by the database.</summary>
    public Task<ImportOutcome> AddBondTradesAsync(IEnumerable<BondTrade> trades, CancellationToken cancellationToken = default) =>
        InsertAllAsync(trades, t => t.Id,
            "INSERT INTO bond_trade (id, trade_date, bond_id, face, clean_price, counterparty) VALUES ($1, $2, $3, $4, $5, $6) ON CONFLICT (id) DO NOTHING",
            t => new object[] { t.Id, t.TradeDate, t.BondId, t.Face, t.CleanPrice, t.Counterparty },
            async (connection, transaction, id) => (await ReadBondTradesAsync(connection, transaction, "WHERE id = $1", id)).SingleOrDefault(),
            cancellationToken);

    private async Task<ImportOutcome> InsertAllAsync<T>(IEnumerable<T> items, Func<T, string> idOf, string insertSql,
        Func<T, object[]> values, Func<NpgsqlConnection, NpgsqlTransaction, string, Task<T?>> readStored, CancellationToken cancellationToken)
        where T : class
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        int added = 0, present = 0;
        foreach (var item in items)
        {
            await using var insert = new NpgsqlCommand(insertSql, connection, transaction);
            foreach (var value in values(item)) insert.Parameters.Add(new() { Value = value });
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                added++;
                continue;
            }
            var stored = await readStored(connection, transaction, idOf(item));
            if (!Equals(stored, item))
            {
                throw new InvalidOperationException($"'{idOf(item)}' is already stored with different fields; nothing was imported");
            }
            present++;
        }
        await transaction.CommitAsync(cancellationToken);
        return new ImportOutcome(added, present);
    }

    public async Task<IReadOnlyDictionary<string, Bond>> LoadBondsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);
        return await LoadBondsAsync(connection, null, null);
    }

    private static async Task<Dictionary<string, Bond>> LoadBondsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string? id)
    {
        var bonds = new Dictionary<string, Bond>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(
            "SELECT id, description, coupon_percent, maturity FROM bond" + (id is null ? "" : " WHERE id = $1"), connection, transaction);
        if (id is not null) command.Parameters.Add(new() { Value = id });
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var bond = new Bond(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2), reader.GetFieldValue<DateOnly>(3));
            bonds[bond.Id] = bond;
        }
        return bonds;
    }

    public async Task<IReadOnlyList<BondTrade>> LoadBondTradesAsync(DateOnly upTo, CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);
        return await ReadBondTradesAsync(connection, null, "WHERE trade_date <= $1", upTo);
    }

    private static async Task<List<BondTrade>> ReadBondTradesAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string where, object value)
    {
        var trades = new List<BondTrade>();
        await using var command = new NpgsqlCommand(
            $"SELECT id, trade_date, bond_id, face, clean_price, counterparty FROM bond_trade {where} ORDER BY trade_date, id", connection, transaction);
        command.Parameters.Add(new() { Value = value });
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            trades.Add(new BondTrade(reader.GetString(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetString(5)));
        }
        return trades;
    }

    /// <summary>Stores one benchmark-yield answer: a rate_fetch row, new yields, and revisions of changed ones.</summary>
    public async Task<FetchOutcome> StoreYieldFetchAsync(ValetYieldObservations observations, CancellationToken cancellationToken = default)
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
            insertFetch.Parameters.Add(new() { Value = ValetClient.BenchmarkYieldGroup });
            insertFetch.Parameters.Add(new() { Value = observations.RequestedFrom });
            insertFetch.Parameters.Add(new() { Value = observations.RequestedTo });
            insertFetch.Parameters.Add(new() { Value = observations.CoveredTo.HasValue ? observations.CoveredTo.Value : DBNull.Value });
            insertFetch.Parameters.Add(new() { Value = observations.FetchedAt });
            insertFetch.Parameters.Add(new() { Value = observations.SourceUrl });
            fetchId = (long)(await insertFetch.ExecuteScalarAsync(cancellationToken))!;
        }

        int added = 0, unchanged = 0, revised = 0;
        foreach (var curve in observations.Days)
        {
            var stored = await ReadCurveAsync(connection, transaction, curve.Date);
            foreach (var (tenor, value) in curve.Points)
            {
                if (!stored.TryGetValue(tenor, out var old))
                {
                    await Execute(connection, transaction, "INSERT INTO yield_point (rate_date, tenor_years, yield_percent, fetch_id) VALUES ($1, $2, $3, $4)",
                        curve.Date, tenor, value, fetchId);
                    added++;
                }
                else if (old == value)
                {
                    unchanged++;
                }
                else
                {
                    await Execute(connection, transaction,
                        "INSERT INTO yield_revision (rate_date, tenor_years, old_yield_percent, new_yield_percent, fetch_id) VALUES ($1, $2, $3, $4, $5)",
                        curve.Date, tenor, old, value, fetchId);
                    await Execute(connection, transaction, "UPDATE yield_point SET yield_percent = $3, fetch_id = $4 WHERE rate_date = $1 AND tenor_years = $2",
                        curve.Date, tenor, value, fetchId);
                    revised++;
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new FetchOutcome(fetchId, observations.Days.Count, added, unchanged, Array.Empty<RateRevision>()) { YieldRevisions = revised };
    }

    public async Task<YieldCurve> LoadCurveAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);
        return new YieldCurve(date, await ReadCurveAsync(connection, null, date));
    }

    public async Task<RateCalendar> LoadYieldCalendarAsync(CancellationToken cancellationToken = default)
    {
        var published = new List<DateOnly>();
        await using (var command = _db.CreateCommand("SELECT DISTINCT rate_date FROM yield_point"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) published.Add(reader.GetFieldValue<DateOnly>(0));
        }
        var fetched = new List<DateRange>();
        await using (var command = _db.CreateCommand(
            "SELECT requested_from, covered_to FROM rate_fetch WHERE covered_to IS NOT NULL AND series_group = $1"))
        {
            command.Parameters.Add(new() { Value = ValetClient.BenchmarkYieldGroup });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                fetched.Add(new DateRange(reader.GetFieldValue<DateOnly>(0), reader.GetFieldValue<DateOnly>(1)));
            }
        }
        return new RateCalendar(published, fetched);
    }

    private static async Task<Dictionary<int, decimal>> ReadCurveAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, DateOnly date)
    {
        var points = new Dictionary<int, decimal>();
        await using var command = new NpgsqlCommand("SELECT tenor_years, yield_percent FROM yield_point WHERE rate_date = $1", connection, transaction);
        command.Parameters.Add(new() { Value = date });
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) points[reader.GetInt32(0)] = reader.GetDecimal(1);
        return points;
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values) command.Parameters.Add(new() { Value = value });
        await command.ExecuteNonQueryAsync();
    }
}
