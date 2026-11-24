using Deskbook.Core.Reporting;
using System.Globalization;
using System.Security.Cryptography;
using Deskbook.Core;
using Deskbook.Core.Bonds;
using Deskbook.Core.Reconciliation;
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
/// 3 book refused because stored data cannot support it, 4 reconciliation done and breaks found.
/// </summary>
public static class App
{
    public const int Ok = 0, Failure = 1, Usage = 2, Refused = 3, Breaks = 4;

    public const string Help = """
        deskbook: the book of a fictional currency desk, priced from Bank of Canada exchange rates.

          deskbook db init
          deskbook rates fetch --from yyyy-MM-dd [--to yyyy-MM-dd]
          deskbook rates revisions
          deskbook trades import <file.csv>
          deskbook trades add --id ID --date yyyy-MM-dd --counterparty NAME --buy CCY:AMOUNT --sell CCY:AMOUNT
          deskbook trades list
          deskbook book [--date yyyy-MM-dd] [--currency CCY]
          deskbook recon run <confirmations.csv>
          deskbook recon runs
          deskbook recon breaks [--run ID]
          deskbook recon resolve --break ID --by NAME --note TEXT
          deskbook yields fetch --from yyyy-MM-dd [--to yyyy-MM-dd]
          deskbook bonds import <bonds.csv>
          deskbook bonds trades import <bond-trades.csv>
          deskbook bonds book [--date yyyy-MM-dd] [--currency CCY]
          deskbook nightly --out DIR [--from yyyy-MM-dd] [--confirmations FILE]

        Add --json to any command for JSON output. The database comes from DESKBOOK_DB (a connection string).
        Exit codes: 0 done, 1 failure, 2 usage error, 3 book refused (data missing), 4 reconciliation found breaks.
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

        await using var services = new Services(env);
        try
        {
            return await DispatchAsync(cli, stdout, services);
        }
        catch (UsageException e)
        {
            return Fail(stderr, cli.Json, Usage, "usage", e.Message);
        }
        catch (BookRefusedException e)
        {
            return Fail(stderr, cli.Json, Refused, "refused", string.Join("; ", e.Reasons), e.Reasons);
        }
        catch (Exception e)
        {
            // Anything else (database, network, a malformed answer, a bad file) is a failure, exit code 1, and is
            // reported in the same shape as every other error, so a script or an agent never sees a stack trace.
            return Fail(stderr, cli.Json, Failure, "failure", e.Message);
        }
    }

    private static async Task<int> DispatchAsync(CliArgs cli, TextWriter stdout, Services services)
    {
        var env = services.Env;
        var command = string.Join(' ', cli.Words.Take(2));
        switch (command)
        {
            case "db init":
                cli.AllowOnly();
                await services.Store.InitializeAsync();
                return Write(stdout, cli.Json, new { status = "ok", action = "schema ready" }, "Schema ready.");

            case "rates fetch":
                return await FetchRatesAsync(cli, stdout, services);

            case "rates revisions":
                cli.AllowOnly();
                var revisions = await services.Store.ListRevisionsAsync();
                return Write(stdout, cli.Json,
                    new { revisions = revisions.Select(r => new { date = Output.Date(r.Date), currency = r.Currency.Code, old_cad_per_unit = r.OldCadPerUnit, new_cad_per_unit = r.NewCadPerUnit }) },
                    revisions.Count == 0 ? "No stored rate has been revised." :
                        string.Join(Environment.NewLine, revisions.Select(r => $"{Output.Date(r.Date)} {r.Currency}: {r.OldCadPerUnit} -> {r.NewCadPerUnit}")));

            case "trades import":
                cli.AllowOnly();
                if (cli.Words.Count != 3) throw new UsageException("trades import takes one file");
                IReadOnlyList<Trade> fromFile;
                using (var reader = new StreamReader(cli.Words[2])) fromFile = TradeCsv.Parse(reader);
                var imported = await services.Store.AddTradesAsync(fromFile);
                return Write(stdout, cli.Json, new { added = imported.Added, already_present = imported.AlreadyPresent },
                    $"{imported.Added} trade(s) added, {imported.AlreadyPresent} already present.");

            case "trades add":
                cli.AllowOnly("id", "date", "counterparty", "buy", "sell");
                var trade = new Trade(cli.RequiredOption("id"), ParseDate(cli.RequiredOption("date")),
                    cli.RequiredOption("counterparty"), ParseLeg(cli.RequiredOption("buy")), ParseLeg(cli.RequiredOption("sell")));
                var one = await services.Store.AddTradesAsync(new[] { trade });
                return Write(stdout, cli.Json, new { added = one.Added, already_present = one.AlreadyPresent },
                    one.Added == 1 ? $"Trade {trade.Id} added." : $"Trade {trade.Id} was already present.");

            case "trades list":
                cli.AllowOnly();
                var trades = await services.Store.ListTradesAsync();
                return Write(stdout, cli.Json,
                    new { fictional = true, trades = trades.Select(TradeJson) },
                    trades.Count == 0 ? "No trades." : string.Join(Environment.NewLine, trades.Select(TradeText)));

            case "book":
            {
                cli.AllowOnly("date", "currency");
                var dateText = cli.Option("date");
                var book = await new BookService(services.Store).BuildAsync(dateText is null ? null : ParseDate(dateText));
                var translation = await TranslateAsync(cli, services, book.Date, book.PreviousDate, book.ValueCad, book.PreviousValueCad,
                    new[] { ("new_trades", book.NewTradesPnlCad), ("revaluation", book.RevaluationPnlCad) });
                return translation is null
                    ? Write(stdout, cli.Json, BookReport.ToJson(book), BookReport.ToText(book))
                    : Write(stdout, cli.Json, WithTranslation(BookReport.ToJson(book), translation),
                        BookReport.ToText(book) + Environment.NewLine + TranslationReport.ToText(translation));
            }

            case "yields fetch":
            {
                cli.AllowOnly("from", "to");
                var from = ParseDate(cli.RequiredOption("from"));
                var to = cli.Option("to") is { } toText ? ParseDate(toText) : DateOnly.FromDateTime(env.Clock.GetUtcNow().UtcDateTime);
                if (to < from) throw new UsageException("--to is before --from");
                var (observations, fromCache) = await new ValetClient(env.Http, env.CacheDirectory, env.Clock).FetchBenchmarkYieldsAsync(from, to);
                var outcome = await services.Bonds.StoreYieldFetchAsync(observations);
                var covered = observations.CoveredTo is null ? "none" : Output.Date(observations.CoveredTo.Value);
                return Write(stdout, cli.Json,
                    new { fetch_id = outcome.FetchId, from_cache = fromCache, days_with_yields = outcome.DaysWithRates, covered_to = observations.CoveredTo is null ? null : covered,
                          yields_added = outcome.RatesAdded, yields_unchanged = outcome.RatesUnchanged, yields_revised = outcome.YieldRevisions, source = BondReport.Source },
                    $"{outcome.DaysWithRates} day(s) with yields{(fromCache ? " (from cache)" : "")}, covered to {covered}: " +
                    $"{outcome.RatesAdded} added, {outcome.RatesUnchanged} unchanged, {outcome.YieldRevisions} revised.");
            }

            case "bonds import":
            {
                cli.AllowOnly();
                if (cli.Words.Count != 3) throw new UsageException("bonds import takes one file");
                IReadOnlyList<Bond> bonds;
                using (var reader = new StreamReader(cli.Words[2])) bonds = BondCsv.ParseBonds(reader);
                var outcome = await services.Bonds.AddBondsAsync(bonds);
                return Write(stdout, cli.Json, new { added = outcome.Added, already_present = outcome.AlreadyPresent },
                    $"{outcome.Added} bond(s) added, {outcome.AlreadyPresent} already present.");
            }

            case "bonds trades":
            {
                cli.AllowOnly();
                if (cli.Words.Count != 4 || cli.Words[2] != "import") throw new UsageException("usage: bonds trades import <file>");
                IReadOnlyList<BondTrade> bondTrades;
                using (var reader = new StreamReader(cli.Words[3])) bondTrades = BondCsv.ParseTrades(reader);
                var outcome = await services.Bonds.AddBondTradesAsync(bondTrades);
                return Write(stdout, cli.Json, new { added = outcome.Added, already_present = outcome.AlreadyPresent },
                    $"{outcome.Added} bond trade(s) added, {outcome.AlreadyPresent} already present.");
            }

            case "bonds book":
            {
                cli.AllowOnly("date", "currency");
                var bondDateText = cli.Option("date");
                var bondBook = await new BondBookService(services.Bonds).BuildAsync(bondDateText is null ? null : ParseDate(bondDateText));
                var bondTranslation = await TranslateAsync(cli, services, bondBook.Date, bondBook.PreviousDate, bondBook.ValueCad, bondBook.PreviousValueCad,
                    new[] { ("held", bondBook.HeldPnlCad), ("new_trades", bondBook.NewTradesPnlCad) });
                return bondTranslation is null
                    ? Write(stdout, cli.Json, BondReport.ToJson(bondBook), BondReport.ToText(bondBook))
                    : Write(stdout, cli.Json, WithTranslation(BondReport.ToJson(bondBook), bondTranslation),
                        BondReport.ToText(bondBook) + Environment.NewLine + TranslationReport.ToText(bondTranslation));
            }

            case "nightly":
                return await NightlyAsync(cli, stdout, services);

            case "recon run":
                return await ReconRunAsync(cli, stdout, services);

            case "recon runs":
                cli.AllowOnly();
                var runs = await services.Recon.ListRunsAsync();
                return Write(stdout, cli.Json,
                    new { runs = runs.Select(r => new { id = r.Id, run_at = Utc(r.RunAt), source_name = r.SourceName, source_sha256 = r.SourceSha256, trades_compared = r.TradesCompared, confirmations_compared = r.ConfirmationsCompared, matched = r.Matched, break_count = r.BreakCount }) },
                    runs.Count == 0 ? "No reconciliation has been run." :
                        string.Join(Environment.NewLine, runs.Select(r => $"#{r.Id} {Utc(r.RunAt)} {r.SourceName}: {r.Matched} matched, {r.BreakCount} break(s)")));

            case "recon breaks":
                return await ReconBreaksAsync(cli, stdout, services);

            case "recon resolve":
                cli.AllowOnly("break", "by", "note");
                var breakText = cli.RequiredOption("break");
                if (!long.TryParse(breakText, NumberStyles.None, CultureInfo.InvariantCulture, out var breakId))
                {
                    throw new UsageException($"'{breakText}' is not a break id");
                }
                await services.Recon.ResolveAsync(breakId, cli.RequiredOption("by"), cli.RequiredOption("note"), env.Clock.GetUtcNow());
                return Write(stdout, cli.Json, new { resolved = breakId }, $"Break #{breakId}: resolution recorded.");

            case "":
            case "help":
                stdout.WriteLine(Help);
                return Ok;

            default:
                throw new UsageException($"unknown command '{command}'; run 'deskbook help'");
        }
    }

    private static async Task<int> FetchRatesAsync(CliArgs cli, TextWriter stdout, Services services)
    {
        var env = services.Env;
        cli.AllowOnly("from", "to");
        var from = ParseDate(cli.RequiredOption("from"));
        var today = DateOnly.FromDateTime(env.Clock.GetUtcNow().UtcDateTime);
        var to = cli.Option("to") is { } toText ? ParseDate(toText) : today;
        if (to < from) throw new UsageException("--to is before --from");

        var client = new ValetClient(env.Http, env.CacheDirectory, env.Clock);
        var (observations, fromCache) = await client.FetchDailyFxAsync(from, to);
        var outcome = await services.Store.StoreFetchAsync(observations);

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

    private static async Task<int> ReconRunAsync(CliArgs cli, TextWriter stdout, Services services)
    {
        var env = services.Env;
        cli.AllowOnly();
        if (cli.Words.Count != 3) throw new UsageException("recon run takes one confirmation file");
        var path = cli.Words[2];
        var bytes = await File.ReadAllBytesAsync(path);
        IReadOnlyList<Confirmation> confirmations;
        using (var reader = new StreamReader(new MemoryStream(bytes))) confirmations = ConfirmationCsv.Parse(reader);

        var trades = await services.Store.ListTradesAsync();
        var result = Reconciler.Reconcile(trades, confirmations);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var runId = await services.Recon.RecordRunAsync(result, Path.GetFileName(path), sha, env.Clock.GetUtcNow());
        var stored = await services.Recon.LoadBreaksAsync(runId);

        Write(stdout, cli.Json,
            new
            {
                fictional = true,
                run_id = runId,
                source_name = Path.GetFileName(path),
                source_sha256 = sha,
                trades_compared = result.TradesCompared,
                confirmations_compared = result.ConfirmationsCompared,
                matched = result.Matched,
                breaks = stored.Select(BreakJson),
            },
            $"Reconciliation #{runId}: {result.Matched} of {result.TradesCompared} trade(s) matched, {result.Breaks.Count} break(s)." +
            string.Concat(stored.Select(b => Environment.NewLine + "  " + ReconReport.Line(b.Id, b.Break, b.Resolved))));
        return result.Breaks.Count == 0 ? Ok : Breaks;
    }

    /// <summary>
    /// The evening job in one command: make sure the schema exists, fetch the last two weeks of exchange rates and
    /// benchmark yields (so late revisions are caught), write the FX book and the bond book of their latest published
    /// days to OUT/book-DATE.json and OUT/bond-book-DATE.json, and reconcile the confirmations file if one is given. A failed step does not stop the next one. Exit code: 1 if any step
    /// failed, else 3 if the book was refused, else 4 if reconciliation found breaks, else 0. The summary goes to
    /// standard output and to OUT/last-run.json.
    /// </summary>
    private static async Task<int> NightlyAsync(CliArgs cli, TextWriter stdout, Services services)
    {
        var env = services.Env;
        cli.AllowOnly("out", "from", "confirmations");
        var outDirectory = cli.RequiredOption("out");
        var today = DateOnly.FromDateTime(env.Clock.GetUtcNow().UtcDateTime);
        var from = cli.Option("from") is { } fromText ? ParseDate(fromText) : today.AddDays(-14);
        var steps = new List<object>();
        var failed = false;
        var refused = false;
        var breaks = false;

        Directory.CreateDirectory(outDirectory);
        await services.Store.InitializeAsync();

        try
        {
            var client = new ValetClient(env.Http, env.CacheDirectory, env.Clock);
            var (observations, fromCache) = await client.FetchDailyFxAsync(from, today);
            var outcome = await services.Store.StoreFetchAsync(observations);
            steps.Add(new { step = "rates", status = "ok", from_cache = fromCache, covered_to = observations.CoveredTo is null ? null : Output.Date(observations.CoveredTo.Value), rates_added = outcome.RatesAdded, revisions = outcome.Revisions.Count });
        }
        catch (Exception e) when (e is not UsageException)
        {
            failed = true;
            steps.Add(new { step = "rates", status = "failure", message = e.Message });
        }

        try
        {
            var (yields, fromCache) = await new ValetClient(env.Http, env.CacheDirectory, env.Clock).FetchBenchmarkYieldsAsync(from, today);
            var outcome = await services.Bonds.StoreYieldFetchAsync(yields);
            steps.Add(new { step = "yields", status = "ok", from_cache = fromCache, covered_to = yields.CoveredTo is null ? null : Output.Date(yields.CoveredTo.Value), yields_added = outcome.RatesAdded, revisions = outcome.YieldRevisions });
        }
        catch (Exception e) when (e is not UsageException)
        {
            failed = true;
            steps.Add(new { step = "yields", status = "failure", message = e.Message });
        }

        try
        {
            var book = await new BookService(services.Store).BuildAsync(null);
            var path = Path.Combine(outDirectory, $"book-{Output.Date(book.Date)}.json");
            await File.WriteAllTextAsync(path, Output.ToJson(BookReport.ToJson(book)) + Environment.NewLine);
            steps.Add(new { step = "book", status = "ok", book_date = Output.Date(book.Date), file = path, pnl_cad = Output.Cents(book.TotalPnlCad) });
        }
        catch (BookRefusedException e)
        {
            refused = true;
            steps.Add(new { step = "book", status = "refused", reasons = e.Reasons });
        }
        catch (Exception e)
        {
            failed = true;
            steps.Add(new { step = "book", status = "failure", message = e.Message });
        }

        try
        {
            var bondBook = await new BondBookService(services.Bonds).BuildAsync(null);
            var path = Path.Combine(outDirectory, $"bond-book-{Output.Date(bondBook.Date)}.json");
            await File.WriteAllTextAsync(path, Output.ToJson(BondReport.ToJson(bondBook)) + Environment.NewLine);
            steps.Add(new { step = "bond book", status = "ok", book_date = Output.Date(bondBook.Date), file = path, pnl_cad = Output.Cents(bondBook.TotalPnlCad), dv01_cad = Output.Cents(bondBook.Dv01Cad) });
        }
        catch (BookRefusedException e)
        {
            refused = true;
            steps.Add(new { step = "bond book", status = "refused", reasons = e.Reasons });
        }
        catch (Exception e)
        {
            failed = true;
            steps.Add(new { step = "bond book", status = "failure", message = e.Message });
        }

        if (cli.Option("confirmations") is { } confirmationsPath)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(confirmationsPath);
                IReadOnlyList<Confirmation> confirmations;
                using (var reader = new StreamReader(new MemoryStream(bytes))) confirmations = ConfirmationCsv.Parse(reader);
                var result = Reconciler.Reconcile(await services.Store.ListTradesAsync(), confirmations);
                var runId = await services.Recon.RecordRunAsync(result, Path.GetFileName(confirmationsPath),
                    Convert.ToHexStringLower(SHA256.HashData(bytes)), env.Clock.GetUtcNow());
                breaks = result.Breaks.Count > 0;
                steps.Add(new { step = "recon", status = breaks ? "breaks" : "ok", run_id = runId, matched = result.Matched, breaks = result.Breaks.Count });
            }
            catch (Exception e)
            {
                failed = true;
                steps.Add(new { step = "recon", status = "failure", message = e.Message });
            }
        }

        var code = failed ? Failure : refused ? Refused : breaks ? Breaks : Ok;
        var summary = Output.ToJson(new { fictional = true, run_at = Utc(env.Clock.GetUtcNow()), exit_code = code, steps });
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "last-run.json"), summary + Environment.NewLine);
        stdout.WriteLine(summary);
        return code;
    }

    /// <summary>
    /// With --currency, the book's CAD figures in that currency, at the stored exchange rates of the book's day and of
    /// its previous day. Without it, null: the book stays in CAD only.
    /// </summary>
    private static async Task<Translation?> TranslateAsync(CliArgs cli, Services services, DateOnly date, DateOnly? previousDate,
        decimal valueCad, decimal previousValueCad, IReadOnlyList<(string, decimal)> partsCad)
    {
        if (cli.Option("currency") is not { } code) return null;
        Currency currency;
        try { currency = new Currency(code); }
        catch (ArgumentException e) { throw new UsageException(e.Message); }
        if (currency == Currency.Cad) throw new UsageException("the book is already in CAD; give another currency");

        var today = await services.Store.LoadRatesAsync(date);
        var previous = previousDate is null ? null : await services.Store.LoadRatesAsync(previousDate.Value);
        return Translation.Translate(currency, today, previous, valueCad, previousValueCad, partsCad);
    }

    /// <summary>The book's JSON with an "in_currency" block added beside its fields, so its shape never changes.</summary>
    private static System.Text.Json.Nodes.JsonObject WithTranslation(object bookJson, Translation translation)
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(bookJson, Output.JsonOptions)!.AsObject();
        node["in_currency"] = System.Text.Json.JsonSerializer.SerializeToNode(TranslationReport.ToJson(translation), Output.JsonOptions);
        return node;
    }

    private static async Task<int> ReconBreaksAsync(CliArgs cli, TextWriter stdout, Services services)
    {
        var env = services.Env;
        cli.AllowOnly("run");
        long runId;
        if (cli.Option("run") is { } runText)
        {
            if (!long.TryParse(runText, NumberStyles.None, CultureInfo.InvariantCulture, out runId)) throw new UsageException($"'{runText}' is not a run id");
        }
        else
        {
            var runs = await services.Recon.ListRunsAsync();
            if (runs.Count == 0) throw new InvalidOperationException("no reconciliation has been run; run 'deskbook recon run <file>'");
            runId = runs[^1].Id;
        }
        var breaks = await services.Recon.LoadBreaksAsync(runId);
        return Write(stdout, cli.Json, new { run_id = runId, breaks = breaks.Select(BreakJson) },
            breaks.Count == 0 ? $"Run #{runId} has no breaks." :
                string.Join(Environment.NewLine, breaks.Select(b => ReconReport.Line(b.Id, b.Break, b.Resolved))));
    }

    private static ReconReport.BreakJson BreakJson(StoredBreak b) =>
        ReconReport.ToJson(b.Id, b.Break, b.Resolutions.Select(r => (r.ResolvedAt, r.ResolvedBy, r.Note)).ToList());

    private static string Utc(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

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

    /// <summary>
    /// What one run of the command needs from the outside, created on first use. One connection pool per run,
    /// closed when the run ends, so a caller that runs many commands in one process (the tests) leaks nothing.
    /// </summary>
    private sealed class Services(AppEnvironment env) : IAsyncDisposable
    {
        private NpgsqlDataSource? _db;

        public AppEnvironment Env { get; } = env;

        public PostgresStore Store => new(DataSource());

        public ReconStore Recon => new(DataSource());

        public BondStore Bonds => new(DataSource());

        private NpgsqlDataSource DataSource()
        {
            if (_db is not null) return _db;
            if (string.IsNullOrWhiteSpace(Env.ConnectionString))
            {
                throw new InvalidOperationException("DESKBOOK_DB is not set (a PostgreSQL connection string)");
            }
            return _db = NpgsqlDataSource.Create(Env.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            if (_db is not null) await _db.DisposeAsync();
        }
    }
}
