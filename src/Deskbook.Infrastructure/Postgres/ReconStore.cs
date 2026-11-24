using Deskbook.Core.Reconciliation;
using Npgsql;

namespace Deskbook.Infrastructure.Postgres;

/// <summary>A stored reconciliation run.</summary>
public sealed record ReconRun(long Id, DateTimeOffset RunAt, string SourceName, string SourceSha256,
    int TradesCompared, int ConfirmationsCompared, int Matched, int BreakCount);

/// <summary>A stored break, with its resolution notes (oldest first). It counts as resolved once it has one.</summary>
public sealed record StoredBreak(long Id, long RunId, Break Break, IReadOnlyList<Resolution> Resolutions)
{
    public bool Resolved => Resolutions.Count > 0;
}

public sealed record Resolution(DateTimeOffset ResolvedAt, string ResolvedBy, string Note);

/// <summary>Reads and writes the reconciliation audit trail. Every write is an INSERT; the schema refuses anything else.</summary>
public sealed class ReconStore
{
    private readonly NpgsqlDataSource _db;

    public ReconStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>Stores a run and its breaks in one transaction and returns the run's id.</summary>
    public async Task<long> RecordRunAsync(ReconciliationResult result, string sourceName, string sourceSha256,
        DateTimeOffset runAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long runId;
        await using (var insertRun = new NpgsqlCommand(
            """
            INSERT INTO recon_run (run_at, source_name, source_sha256, trades_compared, confirmations_compared, matched, break_count)
            VALUES ($1, $2, $3, $4, $5, $6, $7) RETURNING id
            """, connection, transaction))
        {
            foreach (var value in new object[] { runAt, sourceName, sourceSha256, result.TradesCompared, result.ConfirmationsCompared, result.Matched, result.Breaks.Count })
            {
                insertRun.Parameters.Add(new() { Value = value });
            }
            runId = (long)(await insertRun.ExecuteScalarAsync(cancellationToken))!;
        }

        foreach (var b in result.Breaks)
        {
            await using var insertBreak = new NpgsqlCommand(
                """
                INSERT INTO recon_break (run_id, kind, trade_id, confirmation_id, field, desk_value, counterparty_value, detail)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
                """, connection, transaction);
            foreach (var value in new object?[] { runId, b.Kind.ToString(), b.TradeId, b.ConfirmationId, b.Field, b.DeskValue, b.CounterpartyValue, b.Detail })
            {
                insertBreak.Parameters.Add(new() { Value = value ?? DBNull.Value });
            }
            await insertBreak.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return runId;
    }

    public async Task<IReadOnlyList<ReconRun>> ListRunsAsync(CancellationToken cancellationToken = default)
    {
        var runs = new List<ReconRun>();
        await using var command = _db.CreateCommand(
            "SELECT id, run_at, source_name, source_sha256, trades_compared, confirmations_compared, matched, break_count FROM recon_run ORDER BY id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(new ReconRun(reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7)));
        }
        return runs;
    }

    /// <summary>The breaks of one run, with their resolutions, in the order the run found them.</summary>
    public async Task<IReadOnlyList<StoredBreak>> LoadBreaksAsync(long runId, CancellationToken cancellationToken = default)
    {
        var rows = new List<(long Id, Break Break)>();
        await using (var command = _db.CreateCommand(
            "SELECT id, kind, trade_id, confirmation_id, field, desk_value, counterparty_value, detail FROM recon_break WHERE run_id = $1 ORDER BY id"))
        {
            command.Parameters.Add(new() { Value = runId });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
                rows.Add((reader.GetInt64(0), new Break(Enum.Parse<BreakKind>(reader.GetString(1)),
                    Text(2), Text(3), Text(4), Text(5), Text(6), reader.GetString(7))));
            }
        }

        var resolutions = new Dictionary<long, List<Resolution>>();
        await using (var command = _db.CreateCommand(
            """
            SELECT r.break_id, r.resolved_at, r.resolved_by, r.note
            FROM recon_resolution r JOIN recon_break b ON b.id = r.break_id
            WHERE b.run_id = $1 ORDER BY r.id
            """))
        {
            command.Parameters.Add(new() { Value = runId });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var breakId = reader.GetInt64(0);
                if (!resolutions.TryGetValue(breakId, out var list)) resolutions[breakId] = list = new List<Resolution>();
                list.Add(new Resolution(reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        return rows.Select(r => new StoredBreak(r.Id, runId, r.Break,
            resolutions.TryGetValue(r.Id, out var list) ? list : new List<Resolution>())).ToList();
    }

    /// <summary>Adds a resolution note to a break. Throws when the break does not exist.</summary>
    public async Task ResolveAsync(long breakId, string resolvedBy, string note, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resolvedBy) || string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("a resolution needs who resolved it and a note");
        }
        await using var command = _db.CreateCommand(
            """
            INSERT INTO recon_resolution (break_id, resolved_at, resolved_by, note)
            SELECT id, $2, $3, $4 FROM recon_break WHERE id = $1
            """);
        foreach (var value in new object[] { breakId, at, resolvedBy.Trim(), note.Trim() }) command.Parameters.Add(new() { Value = value });
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException($"there is no break {breakId}");
        }
    }
}
