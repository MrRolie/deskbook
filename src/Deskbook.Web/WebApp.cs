using Deskbook.Core;
using Deskbook.Core.Reporting;
using Deskbook.Infrastructure.Postgres;
using Npgsql;

namespace Deskbook.Web;

/// <summary>
/// The page and its JSON twin, read-only: it shows what is stored and never fetches rates or changes data.
///   GET /                  the book page (latest published day, or ?date=yyyy-MM-dd)
///   GET /api/book?date=…   the same JSON as "deskbook book --json"; a refusal is HTTP 409 with its reasons
/// </summary>
public static class WebApp
{
    public static WebApplication Build(string[] args, string connectionString)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        builder.Services.AddSingleton<PostgresStore>();
        var app = builder.Build();

        app.MapGet("/", async (string? date, PostgresStore store) =>
        {
            if (!TryParseDate(date, out var bookDate))
            {
                return Results.Content(BookPage.Message("Bad date", $"'{date}' is not a date in the form yyyy-MM-dd."), "text/html", statusCode: 400);
            }
            var calendar = await store.LoadCalendarAsync();
            try
            {
                var book = await new BookService(store).BuildAsync(bookDate);
                return Results.Content(BookPage.Render(book, calendar), "text/html");
            }
            catch (BookRefusedException refusal)
            {
                return Results.Content(BookPage.Refused(bookDate, refusal.Reasons, calendar), "text/html", statusCode: 409);
            }
        });

        app.MapGet("/api/book", async (string? date, PostgresStore store) =>
        {
            if (!TryParseDate(date, out var bookDate))
            {
                return Results.Json(new { error = "usage", message = $"'{date}' is not a date in the form yyyy-MM-dd" }, Output.JsonOptions, statusCode: 400);
            }
            try
            {
                var book = await new BookService(store).BuildAsync(bookDate);
                return Results.Json(BookReport.ToJson(book), Output.JsonOptions);
            }
            catch (BookRefusedException refusal)
            {
                return Results.Json(new { error = "refused", message = string.Join("; ", refusal.Reasons), reasons = refusal.Reasons }, Output.JsonOptions, statusCode: 409);
            }
        });

        return app;
    }

    private static bool TryParseDate(string? text, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrEmpty(text)) return true;
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
        {
            date = parsed;
            return true;
        }
        return false;
    }
}
