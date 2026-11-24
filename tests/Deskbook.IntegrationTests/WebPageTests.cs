using System.Net;
using System.Text.Json;
using Deskbook.Cli;
using Deskbook.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using static Deskbook.IntegrationTests.Files;

namespace Deskbook.IntegrationTests;

/// <summary>
/// The page, served by the real web application on a free local port, over a real PostgreSQL loaded with the
/// sample book through the command line.
/// </summary>
public class WebPageTests : IClassFixture<TestDatabase>, IAsyncLifetime
{
    private readonly TestDatabase _db;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public WebPageTests(TestDatabase db)
    {
        _db = db;
    }

    public async Task InitializeAsync()
    {
        var valet = new SavedValetResponse(File.ReadAllText(Fixture("valet-fx-2026-11.json")));
        var env = new AppEnvironment(_db.ConnectionString, Path.Combine(Path.GetTempPath(), "deskbook-tests", Guid.NewGuid().ToString("N")),
            new HttpClient(valet), new FixedClock(new DateTimeOffset(2026, 11, 24, 4, 0, 0, TimeSpan.Zero)));
        foreach (var args in new[]
                 {
                     new[] { "db", "init" },
                     new[] { "rates", "fetch", "--from", "2026-11-02", "--to", "2026-11-23" },
                     new[] { "trades", "import", Sample("trades-fictional.csv") },
                 })
        {
            Assert.Equal(App.Ok, await App.RunAsync(args, TextWriter.Null, TextWriter.Null, env));
        }

        _app = WebApp.Build(new[] { "--urls", "http://127.0.0.1:0" }, _db.ConnectionString);
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _http = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task The_page_shows_the_latest_book_with_its_notices()
    {
        var response = await _http.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Book for 2026-11-23", html);
        Assert.Contains("CAD 42,827.80", html);
        Assert.Contains("+4,117.60", html);
        Assert.Contains("Fictional desk", html);
        Assert.Contains("The Bank of Canada does not endorse this project.", html);
    }

    [Fact]
    public async Task A_day_without_rates_is_shown_as_refused_not_as_an_empty_book()
    {
        var response = await _http.GetAsync("/?date=2026-11-11");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("No book for this day", html);
        Assert.Contains("published no rates for 2026-11-11", html);
        Assert.DoesNotContain("<table>", html);
    }

    [Fact]
    public async Task The_page_links_to_the_neighbouring_published_days()
    {
        var html = await _http.GetStringAsync("/?date=2026-11-12");
        Assert.Contains("href=\"/?date=2026-11-10\"", html); // the holiday 2026-11-11 is skipped
        Assert.Contains("href=\"/?date=2026-11-13\"", html);
    }

    [Fact]
    public async Task The_JSON_endpoint_gives_the_same_book_as_the_command_line()
    {
        var stdout = new StringWriter();
        var env = new AppEnvironment(_db.ConnectionString, Path.GetTempPath(), new HttpClient(), TimeProvider.System);
        Assert.Equal(App.Ok, await App.RunAsync(new[] { "book", "--date", "2026-11-23", "--json" }, stdout, TextWriter.Null, env));

        var fromWeb = await _http.GetStringAsync("/api/book?date=2026-11-23");
        Assert.Equal(stdout.ToString().TrimEnd(), fromWeb.TrimEnd());
    }

    [Fact]
    public async Task The_JSON_endpoint_refuses_with_409_and_rejects_a_bad_date_with_400()
    {
        var refused = await _http.GetAsync("/api/book?date=2026-11-11");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var json = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("refused", json.RootElement.GetProperty("error").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await _http.GetAsync("/api/book?date=11/23/2026")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.GetAsync("/?date=<script>")).StatusCode);
    }
}
