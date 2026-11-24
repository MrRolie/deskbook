using System.Globalization;
using Deskbook.Core;
using Deskbook.Infrastructure.Postgres;
using Deskbook.Infrastructure.Valet;
using Npgsql;

namespace Deskbook.Cli;

/// <summary>Where the tool finds its database, its cache and its clock. Tests build their own.</summary>
public sealed record AppEnvironment(string? ConnectionString, string CacheDirectory, HttpClient Http, TimeProvider Clock)
{
    public static AppEnvironment FromProcess()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("deskbook/0.1");
        return new AppEnvironment(
            Environment.GetEnvironmentVariable("DESKBOOK_DB"),
            Environment.GetEnvironmentVariable("DESKBOOK_CACHE") ?? Path.Combine("data", "cache"),
            http,
            TimeProvider.System);
    }
}

/// <summary>
/// The command line's entry point. Exit codes: 0 done, 1 failure (database, network, bad data), 2 usage error,
/// 3 book refused because stored data cannot support it.
/// </summary>
public static class App
{
    public const int Ok = 0, Failure = 1, Usage = 2, Refused = 3;

    public const string Help = """
        deskbook: the book of a fictional currency desk, priced from Bank of Canada exchange rates.

          deskbook db init
          deskbook rates fetch --from yyyy-MM-dd [--to yyyy-MM-dd]
          deskbook rates revisions
          deskbook trades import <file.csv>
          deskbook trades add --id ID --date yyyy-MM-dd --counterparty NAME --buy CCY:AMOUNT --sell CCY:AMOUNT
          deskbook trades list
          deskbook book [--date yyyy-MM-dd]

        Add --json to any command for JSON output. The database comes from DESKBOOK_DB (a connection string).
        Exit codes: 0 done, 1 failure, 2 usage error, 3 book refused (data missing).
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, AppEnvironment env)
    {
        CliArgs cli;
        try
        {
            cli = CliArgs.Parse(args);
        }
        catch (UsageException e)
        {
            return Fail(stderr, false, Usage, "usage", e.Message);
        }

        try
        {
            return await DispatchAsync(cli, stdout, env);
        }
        catch (UsageException e)
        {
            return Fail(stderr, cli.Json, Usage, "usage", e.Message);
        }
        catch (BookRefusedException e)
        {
            return Fail(stderr, cli.Json, Refused, "refused", string.Join("; ", e.Reasons), e.Reasons);
        }
        catch (Exception e) when (e is NpgsqlException or HttpRequestException or TaskCanceledException
                                      or FormatException or InvalidOperationException or ArgumentException or IOException)
        {
            return Fail(stderr, cli.Json, Failure, "failure", e.Message);
        }
    }

    private static async Task<int> DispatchAsync(CliArgs cli, TextWriter stdout, AppEnvironment env)
    {
        var command = string.Join(' ', cli.Words.Take(2));
        switch (command)
        {
            case "db init":
                cli.AllowOnly();
                await Store(env).InitializeAsync();
                return Write(stdout, cli.Json, new { status = "ok", action = "schema ready" }, "Schema ready.");

            case "rates fetch":
                return await FetchRatesAsync(cli, stdout, env);

            case "rates revisions":
                cli.AllowOnly();
                var revisions = await Store(env).ListRevisionsAsync();
                return Write(stdout, cli.Json,
                    new { revisions = revisions.Select(r => new { date = Output.Date(r.Date), currency = r.Currency.Code, old_cad_per_unit = r.OldCadPerUnit, new_cad_per_unit = r.NewCadPerUnit }) },
                    revisions.Count == 0 ? "No stored rate has been revised." :
                        string.Join(Environment.NewLine, revisions.Select(r => $"{Output.Date(r.Date)} {r.Currency}: {r.OldCadPerUnit} -> {r.NewCadPerUnit}")));

            case "trades import":
                cli.AllowOnly();
                if (cli.Words.Count != 3) throw new UsageException("trades import takes one file");
                IReadOnlyList<Trade> fromFile;
                using (var reader = new StreamReader(cli.Words[2])) fromFile = TradeCsv.Parse(reader);
                var imported = await Store(env).AddTradesAsync(fromFile);
                return Write(stdout, cli.Json, new { added = imported.Added, already_present = imported.AlreadyPresent },
                    $"{imported.Added} trade(s) added, {imported.AlreadyPresent} already present.");

            case "trades add":
                cli.AllowOnly("id", "date", "counterparty", "buy", "sell");
                var trade = new Trade(cli.RequiredOption("id"), ParseDate(cli.RequiredOption("date")),
                    cli.RequiredOption("counterparty"), ParseLeg(cli.RequiredOption("buy")), ParseLeg(cli.RequiredOption("sell")));
                var one = await Store(env).AddTradesAsync(new[] { trade });
                return Write(stdout, cli.Json, new { added = one.Added, already_present = one.AlreadyPresent },
                    one.Added == 1 ? $"Trade {trade.Id} added." : $"Trade {trade.Id} was already present.");

            case "trades list":
                cli.AllowOnly();
                var trades = await Store(env).ListTradesAsync();
                return Write(stdout, cli.Json,
                    new { fictional = true, trades = trades.Select(TradeJson) },
                    trades.Count == 0 ? "No trades." : string.Join(Environment.NewLine, trades.Select(TradeText)));

            case "book":
                cli.AllowOnly("date");
                var dateText = cli.Option("date");
                var book = await new BookService(Store(env)).BuildAsync(dateText is null ? null : ParseDate(dateText));
                return Write(stdout, cli.Json, BookReport.ToJson(book), BookReport.ToText(book));

            case "":
            case "help":
                stdout.WriteLine(Help);
                return Ok;

            default:
                throw new UsageException($"unknown command '{command}'; run 'deskbook help'");
        }
    }

    private static async Task<int> FetchRatesAsync(CliArgs cli, TextWriter stdout, AppEnvironment env)
    {
        cli.AllowOnly("from", "to");
        var from = ParseDate(cli.RequiredOption("from"));
        var today = DateOnly.FromDateTime(env.Clock.GetUtcNow().UtcDateTime);
        var to = cli.Option("to") is { } toText ? ParseDate(toText) : today;
        if (to < from) throw new UsageException("--to is before --from");

        var client = new ValetClient(env.Http, env.CacheDirectory, env.Clock);
        var (observations, fromCache) = await client.FetchDailyFxAsync(from, to);
        var outcome = await Store(env).StoreFetchAsync(observations);

        var covered = observations.CoveredTo is null ? "none" : Output.Date(observations.CoveredTo.Value);
        return Write(stdout, cli.Json,
            new
            {
                fetch_id = outcome.FetchId,
                from_cache = fromCache,
                days_with_rates = outcome.DaysWithRates,
                covered_to = observations.CoveredTo is null ? null : covered,
                rates_added = outcome.RatesAdded,
                rates_unchanged = outcome.RatesUnchanged,
                revisions = outcome.Revisions.Select(r => new { date = Output.Date(r.Date), currency = r.Currency.Code, old_cad_per_unit = r.OldCadPerUnit, new_cad_per_unit = r.NewCadPerUnit }),
                source = Attribution.Source,
            },
            $"{outcome.DaysWithRates} day(s) with rates{(fromCache ? " (from cache)" : "")}, covered to {covered}: " +
            $"{outcome.RatesAdded} added, {outcome.RatesUnchanged} unchanged, {outcome.Revisions.Count} revised.");
    }

    private static PostgresStore Store(AppEnvironment env)
    {
        if (string.IsNullOrWhiteSpace(env.ConnectionString))
        {
            throw new InvalidOperationException("DESKBOOK_DB is not set (a PostgreSQL connection string)");
        }
        return new PostgresStore(NpgsqlDataSource.Create(env.ConnectionString));
    }

    private static DateOnly ParseDate(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new UsageException($"'{text}' is not a date in the form yyyy-MM-dd");

    private static Leg ParseLeg(string text)
    {
        var parts = text.Split(':');
        if (parts.Length != 2 || !decimal.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            throw new UsageException($"'{text}' is not CCY:AMOUNT, for example USD:1000000");
        }
        return new Leg(new Currency(parts[0]), amount);
    }

    private static object TradeJson(Trade t) => new
    {
        id = t.Id, trade_date = Output.Date(t.TradeDate), counterparty = t.Counterparty,
        bought_currency = t.Bought.Currency.Code, bought_amount = t.Bought.Amount,
        sold_currency = t.Sold.Currency.Code, sold_amount = t.Sold.Amount,
    };

    private static string TradeText(Trade t) =>
        $"{t.Id} {Output.Date(t.TradeDate)} bought {t.Bought.Currency} {Output.Amount(t.Bought.Amount)} " +
        $"sold {t.Sold.Currency} {Output.Amount(t.Sold.Amount)} with {t.Counterparty}";

    private static int Write(TextWriter stdout, bool json, object jsonValue, string text)
    {
        stdout.WriteLine(json ? Output.ToJson(jsonValue) : text);
        return Ok;
    }

    private static int Fail(TextWriter stderr, bool json, int code, string kind, string message, IReadOnlyList<string>? reasons = null)
    {
        stderr.WriteLine(json
            ? Output.ToJson(new { error = kind, message, reasons = reasons ?? Array.Empty<string>(), exit_code = code })
            : $"deskbook: {kind}: {message}");
        return code;
    }
}
