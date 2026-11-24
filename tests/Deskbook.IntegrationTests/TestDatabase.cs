using Npgsql;

namespace Deskbook.IntegrationTests;

/// <summary>
/// A fresh, empty PostgreSQL schema for one test class, dropped afterwards. The server comes from DESKBOOK_TEST_DB.
/// If that variable is missing or the server cannot be reached, every test fails: these tests never skip, because
/// a skipped database test would report green without having checked anything.
/// </summary>
public sealed class TestDatabase : IAsyncLifetime
{
    private readonly string _schema = "test_" + Guid.NewGuid().ToString("N");
    private string _serverConnectionString = "";

    public string ConnectionString { get; private set; } = "";
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable("DESKBOOK_TEST_DB")
            ?? throw new InvalidOperationException(
                "DESKBOOK_TEST_DB is not set. Integration tests need a real PostgreSQL; run them with scripts/dev.sh.");

        await using (var server = NpgsqlDataSource.Create(_serverConnectionString))
        await using (var create = server.CreateCommand($"CREATE SCHEMA {_schema}"))
        {
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(_serverConnectionString) { SearchPath = _schema }.ConnectionString;
        DataSource = NpgsqlDataSource.Create(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await using var server = NpgsqlDataSource.Create(_serverConnectionString);
        await using var drop = server.CreateCommand($"DROP SCHEMA IF EXISTS {_schema} CASCADE");
        await drop.ExecuteNonQueryAsync();
    }

    public async Task<long> CountAsync(string table)
    {
        await using var command = DataSource.CreateCommand($"SELECT count(*) FROM {table}");
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
