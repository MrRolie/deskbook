using System.Text.Json;
using Deskbook.Cli;
using Npgsql;
using static Deskbook.IntegrationTests.Files;

namespace Deskbook.IntegrationTests;

/// <summary>The bond book through the command line, on a real PostgreSQL, with saved Bank of Canada answers.</summary>
public class BondBookTests : IClassFixture<TestDatabase>, IAsyncLifetime
{
    private readonly TestDatabase _db;
    private readonly SavedValetResponse _valet = new(File.ReadAllText(Fixture("valet-bond-yields-2026-11.json")));
    private readonly AppEnvironment _env;

    public BondBookTests(TestDatabase db)
    {
        _db = db;
        _env = new AppEnvironment(db.ConnectionString, Path.Combine(Path.GetTempPath(), "deskbook-tests", Guid.NewGuid().ToString("N")),
            new HttpClient(_valet), new FixedClock(new DateTimeOffset(2026, 11, 24, 4, 0, 0, TimeSpan.Zero)));
    }

    private async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await App.RunAsync(args, stdout, stderr, _env);
        return (code, stdout.ToString(), stderr.ToString());
    }

    public async Task InitializeAsync()
    {
        Assert.Equal(App.Ok, (await Run("db", "init")).Code);
        await using (var clear = _db.DataSource.CreateCommand("TRUNCATE bond_trade, bond, yield_point, yield_revision, rate, rate_revision, rate_fetch CASCADE"))
        {
            await clear.ExecuteNonQueryAsync();
        }
        foreach (var args in new[]
                 {
                     new[] { "yields", "fetch", "--from", "2026-11-02", "--to", "2026-11-20" },
                     new[] { "bonds", "import", Sample("bonds-fictional.csv") },
                     new[] { "bonds", "trades", "import", Sample("bond-trades-fictional.csv") },
                 })
        {
            var result = await Run(args);
            Assert.True(result.Code == App.Ok, result.Err);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_bond_book_comes_out_with_the_reference_numbers()
    {
        var (code, output, error) = await Run("bonds", "book", "--json");
        Assert.True(code == App.Ok, error);

        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.True(root.GetProperty("fictional").GetBoolean());
        Assert.Equal("2026-11-20", root.GetProperty("book_date").GetString());
        Assert.Equal(72_035.43m, root.GetProperty("value_cad").GetDecimal());
        Assert.Equal(81_890.77m, root.GetProperty("pnl").GetProperty("total_cad").GetDecimal());
        Assert.Equal(5_609.47m, root.GetProperty("dv01_cad").GetDecimal());
        Assert.Contains("Not market quotes", root.GetProperty("model").GetString());
    }

    [Fact]
    public async Task The_fetch_asks_the_Bank_for_the_benchmark_group()
    {
        Assert.Contains(_valet.Urls, url => url.StartsWith("https://www.bankofcanada.ca/valet/observations/group/bond_yields_benchmark/json?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_yields_fetch_does_not_count_as_an_exchange_rate_fetch()
    {
        // Only yields were fetched: the FX book must still say its rates were never fetched.
        var (code, _, error) = await Run("book", "--date", "2026-11-20");
        Assert.Equal(App.Refused, code);
        Assert.Contains("never fetched", error);
    }

    [Fact]
    public async Task A_day_whose_yields_are_not_published_yet_is_refused()
    {
        Assert.Equal(App.Refused, (await Run("bonds", "book", "--date", "2026-11-23")).Code);
    }

    [Theory]
    [InlineData("UPDATE bond_trade SET face = 1")]
    [InlineData("DELETE FROM bond")]
    public async Task Bonds_and_bond_trades_cannot_be_changed(string sql)
    {
        await using var command = _db.DataSource.CreateCommand(sql);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("append-only", error.MessageText);
    }
}
