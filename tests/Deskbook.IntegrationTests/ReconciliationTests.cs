using System.Security.Cryptography;
using System.Text.Json;
using Deskbook.Cli;
using Npgsql;
using static Deskbook.IntegrationTests.Files;

namespace Deskbook.IntegrationTests;

/// <summary>Reconciliation through the command line, and its audit trail, on a real PostgreSQL.</summary>
public class ReconciliationTests : IClassFixture<TestDatabase>, IAsyncLifetime
{
    private readonly TestDatabase _db;
    private readonly AppEnvironment _env;

    public ReconciliationTests(TestDatabase db)
    {
        _db = db;
        _env = new AppEnvironment(db.ConnectionString, Path.GetTempPath(), new HttpClient(),
            new FixedClock(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)));
    }

    public async Task InitializeAsync()
    {
        var init = await Run("db", "init");
        Assert.True(init.Code == App.Ok, init.Err);
        await using (var clear = _db.DataSource.CreateCommand("TRUNCATE trade, recon_resolution, recon_break, recon_run"))
        {
            await clear.ExecuteNonQueryAsync();
        }
        var import = await Run("trades", "import", Sample("trades-fictional.csv"));
        Assert.True(import.Code == App.Ok, import.Err);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await App.RunAsync(args, stdout, stderr, _env);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task A_run_with_breaks_exits_4_and_records_the_file_it_read()
    {
        var (code, output, _) = await Run("recon", "run", Sample("confirmations-fictional.csv"), "--json");

        Assert.Equal(App.Breaks, code);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(4, json.RootElement.GetProperty("breaks").GetArrayLength());
        var expectedSha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Sample("confirmations-fictional.csv"))));
        Assert.Equal(expectedSha, json.RootElement.GetProperty("source_sha256").GetString());
        Assert.Equal(4, await _db.CountAsync("recon_break"));
    }

    [Fact]
    public async Task A_clean_file_exits_0()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
        await File.WriteAllTextAsync(path, string.Join('\n',
            "confirmation_id,trade_id,trade_date,counterparty,desk_bought_currency,desk_bought_amount,desk_sold_currency,desk_sold_amount",
            "C1,FICT-0001,2026-11-03,Northwind Pension Fund (fictional),USD,1000000,CAD,1399000"));
        await using (var clear = _db.DataSource.CreateCommand("TRUNCATE trade"))
        {
            await clear.ExecuteNonQueryAsync();
        }
        await Run("trades", "add", "--id", "FICT-0001", "--date", "2026-11-03", "--counterparty", "Northwind Pension Fund (fictional)",
            "--buy", "USD:1000000", "--sell", "CAD:1399000");

        Assert.Equal(App.Ok, (await Run("recon", "run", path)).Code);
    }

    [Fact]
    public async Task Every_run_is_kept_and_a_resolution_is_shown_with_its_break()
    {
        await Run("recon", "run", Sample("confirmations-fictional.csv"));
        await Run("recon", "run", Sample("confirmations-fictional.csv"));

        var (_, runsOut, _) = await Run("recon", "runs", "--json");
        using (var runs = JsonDocument.Parse(runsOut))
        {
            Assert.Equal(2, runs.RootElement.GetProperty("runs").GetArrayLength());
        }

        var (_, breaksOut, _) = await Run("recon", "breaks", "--json");
        long firstBreak;
        using (var breaks = JsonDocument.Parse(breaksOut))
        {
            firstBreak = breaks.RootElement.GetProperty("breaks")[0].GetProperty("break_id").GetInt64();
        }

        Assert.Equal(App.Ok, (await Run("recon", "resolve", "--break", firstBreak.ToString(), "--by", "ops (fictional)",
            "--note", "amended confirmation requested")).Code);

        var (_, afterOut, _) = await Run("recon", "breaks", "--json");
        using var after = JsonDocument.Parse(afterOut);
        var resolved = after.RootElement.GetProperty("breaks")[0];
        Assert.True(resolved.GetProperty("resolved").GetBoolean());
        Assert.Equal("ops (fictional)", resolved.GetProperty("resolutions")[0].GetProperty("resolved_by").GetString());
        Assert.False(after.RootElement.GetProperty("breaks")[1].GetProperty("resolved").GetBoolean());
    }

    [Fact]
    public async Task Resolving_a_break_that_does_not_exist_fails()
    {
        Assert.Equal(App.Failure, (await Run("recon", "resolve", "--break", "999999", "--by", "ops", "--note", "x")).Code);
    }

    [Theory]
    [InlineData("UPDATE recon_break SET detail = 'edited'")]
    [InlineData("DELETE FROM recon_run")]
    [InlineData("DELETE FROM recon_resolution")]
    [InlineData("UPDATE trade SET counterparty = 'edited'")]
    [InlineData("DELETE FROM trade")]
    public async Task The_audit_trail_and_the_trades_cannot_be_changed_or_deleted(string sql)
    {
        await Run("recon", "run", Sample("confirmations-fictional.csv"));
        long breakId;
        await using (var first = _db.DataSource.CreateCommand("SELECT min(id) FROM recon_break"))
        {
            breakId = (long)(await first.ExecuteScalarAsync())!;
        }
        await Run("recon", "resolve", "--break", breakId.ToString(), "--by", "ops", "--note", "seen");

        await using var command = _db.DataSource.CreateCommand(sql);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("append-only", error.MessageText);
    }
}
