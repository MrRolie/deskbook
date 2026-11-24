using System.Text.Json;
using Deskbook.Cli;
using static Deskbook.IntegrationTests.Files;

namespace Deskbook.IntegrationTests;

/// <summary>
/// The command line end to end, against a real PostgreSQL. The only stand-in is the Bank of Canada itself:
/// a saved Valet response (tests/Fixtures) answers instead, so the tests make no network request.
/// </summary>
public class CommandLineTests : IClassFixture<TestDatabase>
{
    private readonly TestDatabase _db;
    private readonly SavedValetResponse _valet = new(File.ReadAllText(Fixture("valet-fx-2026-11.json")));
    private readonly AppEnvironment _env;

    public CommandLineTests(TestDatabase db)
    {
        _db = db;
        var cache = Path.Combine(Path.GetTempPath(), "deskbook-tests", Guid.NewGuid().ToString("N"));
        _env = new AppEnvironment(db.ConnectionString, cache, new HttpClient(_valet),
            new FixedClock(new DateTimeOffset(2026, 11, 24, 4, 0, 0, TimeSpan.Zero)));
    }

    private async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await App.RunAsync(args, stdout, stderr, _env);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private async Task LoadSampleBook()
    {
        Assert.Equal(App.Ok, (await Run("db", "init")).Code);
        await using (var clear = _db.DataSource.CreateCommand("TRUNCATE trade, rate, rate_revision, rate_fetch CASCADE"))
        {
            await clear.ExecuteNonQueryAsync();
        }
        Assert.Equal(App.Ok, (await Run("rates", "fetch", "--from", "2026-11-02", "--to", "2026-11-23")).Code);
        Assert.Equal(App.Ok, (await Run("trades", "import", Sample("trades-fictional.csv"))).Code);
    }

    [Fact]
    public async Task The_sample_book_comes_out_as_JSON_with_the_hand_checked_numbers()
    {
        await LoadSampleBook();
        var (code, output, _) = await Run("book", "--date", "2026-11-23", "--json");

        Assert.Equal(App.Ok, code);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.True(root.GetProperty("fictional").GetBoolean());
        Assert.Equal("2026-11-20", root.GetProperty("previous_date").GetString());
        Assert.Equal(42_827.80m, root.GetProperty("value_cad").GetDecimal());
        Assert.Equal(4_117.60m, root.GetProperty("pnl").GetProperty("total_cad").GetDecimal());
        Assert.Contains("Bank of Canada", root.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Running_the_book_twice_gives_the_same_output()
    {
        await LoadSampleBook();
        var first = await Run("book", "--json");
        var second = await Run("book", "--json");
        Assert.Equal(App.Ok, first.Code);
        Assert.Equal(first.Out, second.Out);
    }

    [Fact]
    public async Task A_day_with_no_published_rates_is_refused_with_exit_code_3()
    {
        await LoadSampleBook();
        var (code, _, error) = await Run("book", "--date", "2026-11-11", "--json");

        Assert.Equal(App.Refused, code);
        using var json = JsonDocument.Parse(error);
        Assert.Equal("refused", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_day_never_fetched_is_refused()
    {
        await LoadSampleBook();
        Assert.Equal(App.Refused, (await Run("book", "--date", "2026-11-24")).Code);
    }

    [Fact]
    public async Task Fetching_the_same_range_twice_the_same_day_reads_the_cache()
    {
        await LoadSampleBook();
        var callsAfterFirst = _valet.Calls;
        var (code, output, _) = await Run("rates", "fetch", "--from", "2026-11-02", "--to", "2026-11-23", "--json");

        Assert.Equal(App.Ok, code);
        Assert.Equal(callsAfterFirst, _valet.Calls);
        using var json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.GetProperty("from_cache").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("rates_added").GetInt32());
    }

    [Fact]
    public async Task The_fetch_asks_the_Bank_for_the_daily_group_and_the_range()
    {
        await LoadSampleBook();
        Assert.Contains(_valet.Urls, url =>
            url == "https://www.bankofcanada.ca/valet/observations/group/FX_RATES_DAILY/json?start_date=2026-11-02&end_date=2026-11-23");
    }

    [Theory]
    [InlineData("book", "--when", "2026-11-23")]
    [InlineData("book", "--date", "23/11/2026")]
    [InlineData("trades", "add", "--id", "X")]
    [InlineData("nonsense")]
    public async Task A_bad_command_line_exits_with_code_2(params string[] args)
    {
        Assert.Equal(App.Usage, (await Run(args)).Code);
    }

    [Fact]
    public async Task A_trade_added_on_the_command_line_shows_in_the_next_book()
    {
        await LoadSampleBook();
        var added = await Run("trades", "add", "--id", "FICT-0009", "--date", "2026-11-23",
            "--counterparty", "Northwind Pension Fund (fictional)", "--buy", "EUR:100000", "--sell", "CAD:156700");
        Assert.Equal(App.Ok, added.Code);

        var (_, output, _) = await Run("book", "--json");
        using var json = JsonDocument.Parse(output);
        var newTrades = json.RootElement.GetProperty("new_trades").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { "FICT-0008", "FICT-0009" }, newTrades);
        // 100,000 x 1.5672 - 156,700 = 20 added to the day's new-trade P&L of 260.
        Assert.Equal(280m, json.RootElement.GetProperty("pnl").GetProperty("new_trades_cad").GetDecimal());
    }
}

public class CurrencyCommandLineTests : IClassFixture<TestDatabase>
{
    private readonly TestDatabase _db;

    public CurrencyCommandLineTests(TestDatabase db)
    {
        _db = db;
    }

    [Fact]
    public async Task The_book_can_be_shown_in_a_second_currency_and_CAD_is_refused_as_one()
    {
        var env = new AppEnvironment(_db.ConnectionString, Path.Combine(Path.GetTempPath(), "deskbook-tests", Guid.NewGuid().ToString("N")),
            new HttpClient(ValetByGroup.Saved()), new FixedClock(new DateTimeOffset(2026, 11, 24, 4, 0, 0, TimeSpan.Zero)));
        async Task<(int Code, string Out)> Run(params string[] args)
        {
            var stdout = new StringWriter();
            return (await App.RunAsync(args, stdout, TextWriter.Null, env), stdout.ToString());
        }

        Assert.Equal(App.Ok, (await Run("db", "init")).Code);
        Assert.Equal(App.Ok, (await Run("rates", "fetch", "--from", "2026-11-02", "--to", "2026-11-23")).Code);
        Assert.Equal(App.Ok, (await Run("trades", "import", Files.Sample("trades-fictional.csv"))).Code);

        var (code, output) = await Run("book", "--date", "2026-11-23", "--currency", "USD", "--json");
        Assert.Equal(App.Ok, code);
        using var json = System.Text.Json.JsonDocument.Parse(output);
        var usd = json.RootElement.GetProperty("in_currency");
        Assert.Equal("USD", usd.GetProperty("currency").GetString());
        Assert.Equal(29_805.69m, usd.GetProperty("value").GetDecimal());
        Assert.Equal(-82.75m, usd.GetProperty("pnl").GetProperty("translation").GetDecimal());
        Assert.Equal(42_827.80m, json.RootElement.GetProperty("book").GetProperty("value_cad").GetDecimal());

        Assert.Equal(App.Usage, (await Run("book", "--currency", "CAD")).Code);
        Assert.Equal(App.Usage, (await Run("book", "--currency", "usd")).Code);
    }
}
