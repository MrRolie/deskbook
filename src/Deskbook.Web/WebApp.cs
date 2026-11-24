using System.Globalization;
using Deskbook.Core;
using Deskbook.Core.Bonds;
using Deskbook.Core.Reporting;
using Deskbook.Infrastructure.Postgres;
using Deskbook.Web.Pages;
using Npgsql;

namespace Deskbook.Web;

/// <summary>
/// The desk's pages and their JSON twins, read-only: they show what is stored and never fetch or change data.
///   GET /          the FX book (?date=yyyy-MM-dd, ?ccy=USD)      GET /api/book    same JSON as "deskbook book --json"
///   GET /bonds     the bond book (same parameters)               GET /api/bonds   same JSON as "deskbook bonds book --json"
///   GET /breaks    the latest reconciliation's breaks
/// A refused book is HTTP 409 with its reasons; an unreadable date or currency 400; an unreadable database 503.
/// </summary>
public static class WebApp
{
    /// <summary>How many published days the P&amp;L history shows.</summary>
    private const int PnlDays = 20;

    public static WebApplication Build(string[] args, string connectionString)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        builder.Services.AddSingleton<PostgresStore>();
        builder.Services.AddSingleton<BondStore>();
        builder.Services.AddSingleton<ReconStore>();
        var app = builder.Build();

        // A database that is down or not yet initialised is an answer, not a crash: 503 with what to do.
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (NpgsqlException e) when (!context.Response.HasStarted)
            {
                var message = $"The book's database cannot be read ({e.Message}). If it is new, run 'deskbook db init'; the evening job does it.";
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    await context.Response.WriteAsJsonAsync(new { error = "unavailable", message }, Output.JsonOptions);
                }
                else
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync(Layout.Page("Database unavailable", Tab.Fx, null,
                        Views.Message("Database unavailable", message), Attribution.Source));
                }
            }
        });

        app.MapGet("/", async (string? date, string? ccy, PostgresStore store) =>
        {
            if (!TryParse(date, ccy, out var bookDate, out var currency, out var problem)) return BadRequest(problem!, Tab.Fx);
            var nav = new Nav("/", currency?.Code);
            var calendar = await store.LoadCalendarAsync();
            var day = bookDate ?? calendar.LatestPublished;
            var history = day is null ? new List<PnlDay>() : await FxHistoryAsync(store, calendar, day.Value);
            var currencies = day is null ? Array.Empty<string>() : await CurrenciesAsync(store, day.Value);
            try
            {
                var book = await new BookService(store).BuildAsync(bookDate);
                var (translation, untranslated) = await TranslateAsync(store, currency, book.Date, book.PreviousDate, book.ValueCad, book.PreviousValueCad,
                    new[] { ("new_trades", book.NewTradesPnlCad), ("revaluation", book.RevaluationPnlCad) });
                return Html(Layout.Page($"FX book, {Pages.Html.LongDate(book.Date)}", Tab.Fx, currency?.Code,
                    Views.FxBook(book, history, translation, nav, calendar, currencies, untranslated), Attribution.Source));
            }
            catch (BookRefusedException refusal)
            {
                return Html(Layout.Page("No book", Tab.Fx, currency?.Code,
                    Views.Refused("FX book", day, refusal.Reasons, history, nav, calendar, currencies), Attribution.Source), 409);
            }
        });

        app.MapGet("/bonds", async (string? date, string? ccy, BondStore bonds, PostgresStore store) =>
        {
            if (!TryParse(date, ccy, out var bookDate, out var currency, out var problem)) return BadRequest(problem!, Tab.Bonds);
            var nav = new Nav("/bonds", currency?.Code);
            var calendar = await bonds.LoadYieldCalendarAsync();
            var day = bookDate ?? calendar.LatestPublished;
            var history = day is null ? new List<PnlDay>() : await BondHistoryAsync(bonds, calendar, day.Value);
            var currencies = day is null ? Array.Empty<string>() : await CurrenciesAsync(store, day.Value);
            try
            {
                var book = await new BondBookService(bonds).BuildAsync(bookDate);
                var today = await bonds.LoadCurveAsync(book.Date);
                var before = book.PreviousDate is null ? null : await bonds.LoadCurveAsync(book.PreviousDate.Value);
                var (translation, untranslated) = await TranslateAsync(store, currency, book.Date, book.PreviousDate, book.ValueCad, book.PreviousValueCad,
                    new[] { ("held", book.HeldPnlCad), ("new_trades", book.NewTradesPnlCad) });
                return Html(Layout.Page($"Bond book, {Pages.Html.LongDate(book.Date)}", Tab.Bonds, currency?.Code,
                    Views.BondBook(book, history, translation, nav, calendar, currencies, today, before, untranslated), BondReport.Source));
            }
            catch (BookRefusedException refusal)
            {
                return Html(Layout.Page("No bond book", Tab.Bonds, currency?.Code,
                    Views.Refused("Bond book", day, refusal.Reasons, history, nav, calendar, currencies), BondReport.Source), 409);
            }
        });

        app.MapGet("/breaks", async (ReconStore recon) =>
        {
            var runs = await recon.ListRunsAsync();
            var run = runs.Count == 0 ? null : runs[^1];
            var breaks = run is null ? Array.Empty<StoredBreak>() : await recon.LoadBreaksAsync(run.Id);
            return Html(Layout.Page("Breaks", Tab.Breaks, null, Views.Breaks(run, breaks), Attribution.Source));
        });

        app.MapGet("/api/book", async (string? date, PostgresStore store) =>
        {
            if (!TryParse(date, null, out var bookDate, out _, out var problem)) return Results.Json(new { error = "usage", message = problem }, Output.JsonOptions, statusCode: 400);
            try
            {
                return Results.Json(BookReport.ToJson(await new BookService(store).BuildAsync(bookDate)), Output.JsonOptions);
            }
            catch (BookRefusedException refusal)
            {
                return Results.Json(new { error = "refused", message = string.Join("; ", refusal.Reasons), reasons = refusal.Reasons }, Output.JsonOptions, statusCode: 409);
            }
        });

        app.MapGet("/api/bonds", async (string? date, BondStore bonds) =>
        {
            if (!TryParse(date, null, out var bookDate, out _, out var problem)) return Results.Json(new { error = "usage", message = problem }, Output.JsonOptions, statusCode: 400);
            try
            {
                return Results.Json(BondReport.ToJson(await new BondBookService(bonds).BuildAsync(bookDate)), Output.JsonOptions);
            }
            catch (BookRefusedException refusal)
            {
                return Results.Json(new { error = "refused", message = string.Join("; ", refusal.Reasons), reasons = refusal.Reasons }, Output.JsonOptions, statusCode: 409);
            }
        });

        return app;
    }

    /// <summary>
    /// The book in the second currency, or, when that currency has no rate for the day or the day before, a note
    /// saying so: the book itself exists and is still shown in CAD.
    /// </summary>
    private static async Task<(Translation? Translation, string Note)> TranslateAsync(PostgresStore store, Currency? currency, DateOnly date,
        DateOnly? previousDate, decimal valueCad, decimal previousValueCad, IReadOnlyList<(string, decimal)> partsCad)
    {
        if (currency is null) return (null, "");
        try
        {
            return (Translation.Translate(currency.Value, await store.LoadRatesAsync(date),
                previousDate is null ? null : await store.LoadRatesAsync(previousDate.Value), valueCad, previousValueCad, partsCad), "");
        }
        catch (BookRefusedException refusal)
        {
            return (null, Views.Message($"Not shown in {currency.Value.Code}", string.Join("; ", refusal.Reasons) + ". The book above is in CAD."));
        }
    }

    private static IResult Html(string page, int status = 200) => Results.Content(page, "text/html; charset=utf-8", statusCode: status);

    private static IResult BadRequest(string problem, Tab tab) =>
        Html(Layout.Page("Bad request", tab, null, Views.Message("That address is not understood", problem), Attribution.Source), 400);

    private static bool TryParse(string? date, string? ccy, out DateOnly? bookDate, out Currency? currency, out string? problem)
    {
        bookDate = null;
        currency = null;
        problem = null;
        if (!string.IsNullOrEmpty(date))
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                problem = $"'{date}' is not a date in the form yyyy-MM-dd.";
                return false;
            }
            bookDate = parsed;
        }
        if (!string.IsNullOrEmpty(ccy) && ccy != "CAD")
        {
            try { currency = new Currency(ccy); }
            catch (ArgumentException) { problem = $"'{ccy}' is not a three-letter currency code."; return false; }
        }
        return true;
    }

    /// <summary>Each published day's P&amp;L in the history window, for the history pane; null where the book is refused.</summary>
    private static async Task<List<PnlDay>> FxHistoryAsync(PostgresStore store, RateCalendar calendar, DateOnly end)
    {
        var days = new List<PnlDay>();
        foreach (var d in Window(calendar, end))
        {
            try { days.Add(new PnlDay(d, (await new BookService(store).BuildAsync(d)).TotalPnlCad)); }
            catch (BookRefusedException) { days.Add(new PnlDay(d, null)); }
        }
        if (!calendar.PublishedDates.Contains(end)) days.Add(new PnlDay(end, null));
        return days.OrderBy(d => d.Date).ToList();
    }

    private static async Task<List<PnlDay>> BondHistoryAsync(BondStore bonds, RateCalendar calendar, DateOnly end)
    {
        var days = new List<PnlDay>();
        foreach (var d in Window(calendar, end))
        {
            try { days.Add(new PnlDay(d, (await new BondBookService(bonds).BuildAsync(d)).TotalPnlCad)); }
            catch (BookRefusedException) { days.Add(new PnlDay(d, null)); }
        }
        if (!calendar.PublishedDates.Contains(end)) days.Add(new PnlDay(end, null));
        return days.OrderBy(d => d.Date).ToList();
    }

    /// <summary>
    /// The published days the history shows: the latest <see cref="PnlDays"/>, so every day is seen in the same
    /// strip; or, for an older day, the <see cref="PnlDays"/> ending at it.
    /// </summary>
    private static IEnumerable<DateOnly> Window(RateCalendar calendar, DateOnly day)
    {
        var latest = calendar.PublishedDates.TakeLast(PnlDays).ToList();
        return latest.Count > 0 && day >= latest[0] ? latest : calendar.PublishedDates.Where(d => d <= day).TakeLast(PnlDays);
    }

    /// <summary>The currencies the picker offers for a day: CAD, then every currency with a rate that day.</summary>
    private static async Task<IReadOnlyList<string>> CurrenciesAsync(PostgresStore store, DateOnly date)
    {
        var rates = await store.LoadRatesAsync(date);
        if (rates.Count == 0)
        {
            var calendar = await store.LoadCalendarAsync();
            var latest = calendar.PublishedDates.Where(d => d <= date).Select(d => (DateOnly?)d).LastOrDefault();
            if (latest is not null) rates = await store.LoadRatesAsync(latest.Value);
        }
        return new[] { "CAD" }.Concat(rates.All().Select(r => r.Currency.Code)).ToList();
    }
}
