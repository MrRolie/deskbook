using System.Text.Json;
using Deskbook.Cli;
using static Deskbook.IntegrationTests.Files;

namespace Deskbook.IntegrationTests;

/// <summary>The evening job, end to end on a real PostgreSQL, with the Bank replaced by a saved answer or an outage.</summary>
public class NightlyTests : IClassFixture<TestDatabase>, IAsyncLifetime
{
    private readonly TestDatabase _db;
    private readonly string _out = Path.Combine(Path.GetTempPath(), "deskbook-tests", Guid.NewGuid().ToString("N"), "out");
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 24, 0, 30, 0, TimeSpan.Zero));

    public NightlyTests(TestDatabase db)
    {
        _db = db;
    }

    private AppEnvironment Env(HttpMessageHandler valet) =>
        new(_db.ConnectionString, Path.Combine(_out, "..", "cache"), new HttpClient(valet), _clock);

    private static async Task<(int Code, string Out)> Run(AppEnvironment env, params string[] args)
    {
        var stdout = new StringWriter();
        var code = await App.RunAsync(args, stdout, TextWriter.Null, env);
        return (code, stdout.ToString());
    }

    public async Task InitializeAsync()
    {
        var env = Env(ValetByGroup.Saved());
        Assert.Equal(App.Ok, (await Run(env, "db", "init")).Code);
        await using (var clear = _db.DataSource.CreateCommand("TRUNCATE trade, rate, rate_revision, rate_fetch, recon_resolution, recon_break, recon_run, bond_trade, bond, yield_point, yield_revision CASCADE"))
        {
            await clear.ExecuteNonQueryAsync();
        }
        Assert.Equal(App.Ok, (await Run(env, "trades", "import", Sample("trades-fictional.csv"))).Code);
        Assert.Equal(App.Ok, (await Run(env, "bonds", "import", Sample("bonds-fictional.csv"))).Code);
        Assert.Equal(App.Ok, (await Run(env, "bonds", "trades", "import", Sample("bond-trades-fictional.csv"))).Code);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_clean_evening_writes_the_latest_book_and_exits_0()
    {
        var env = Env(ValetByGroup.Saved());
        var (code, summary) = await Run(env, "nightly", "--out", _out, "--from", "2026-11-02");

        Assert.Equal(App.Ok, code);
        var bookFile = Path.Combine(_out, "book-2026-11-23.json");
        Assert.True(File.Exists(bookFile));
        Assert.True(File.Exists(Path.Combine(_out, "last-run.json")));

        // The file is exactly what "book --json" prints for that day.
        var (_, printed) = await Run(env, "book", "--date", "2026-11-23", "--json");
        Assert.Equal(printed, await File.ReadAllTextAsync(bookFile));
        // And the bond book of the latest day with yields (they are published a day later than exchange rates here).
        var bondFile = Path.Combine(_out, "bond-book-2026-11-20.json");
        var (_, bondPrinted) = await Run(env, "bonds", "book", "--date", "2026-11-20", "--json");
        Assert.Equal(bondPrinted, await File.ReadAllTextAsync(bondFile));

        using var json = JsonDocument.Parse(summary);
        Assert.Equal(0, json.RootElement.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task Breaks_in_the_confirmations_make_the_job_exit_4()
    {
        var env = Env(ValetByGroup.Saved());
        var (code, summary) = await Run(env, "nightly", "--out", _out, "--from", "2026-11-02",
            "--confirmations", Sample("confirmations-fictional.csv"));

        Assert.Equal(App.Breaks, code);
        Assert.Contains("\"breaks\": 4", summary);
    }

    [Fact]
    public async Task When_the_Bank_is_down_the_job_fails_but_still_writes_the_book_from_stored_rates()
    {
        var up = Env(ValetByGroup.Saved());
        Assert.Equal(App.Ok, (await Run(up, "rates", "fetch", "--from", "2026-11-02", "--to", "2026-11-23")).Code);
        Assert.Equal(App.Ok, (await Run(up, "yields", "fetch", "--from", "2026-11-02", "--to", "2026-11-20")).Code);

        var down = new AppEnvironment(_db.ConnectionString, Path.Combine(_out, "..", "empty-cache"), new HttpClient(new UnavailableValet()), _clock);
        var (code, summary) = await Run(down, "nightly", "--out", _out, "--from", "2026-11-02");

        Assert.Equal(App.Failure, code);
        Assert.Contains("Valet answered 503", summary);
        Assert.True(File.Exists(Path.Combine(_out, "book-2026-11-23.json")));
    }

    [Fact]
    public async Task With_no_rates_at_all_the_book_is_refused_and_the_failure_wins()
    {
        var down = new AppEnvironment(_db.ConnectionString, Path.Combine(_out, "..", "empty-cache"), new HttpClient(new UnavailableValet()), _clock);
        var (code, summary) = await Run(down, "nightly", "--out", _out);

        Assert.Equal(App.Failure, code);
        Assert.Contains("\"status\": \"refused\"", summary);
    }
}
