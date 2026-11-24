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
        var valet = ValetByGroup.Saved();
        var env = new AppEnvironment(_db.ConnectionString, Path.Combine(Path.GetTempPath(), "deskbook-tests", Guid.NewGuid().ToString("N")),
            new HttpClient(valet), new FixedClock(new DateTimeOffset(2026, 11, 24, 4, 0, 0, TimeSpan.Zero)));
        foreach (var args in new[]
                 {
                     new[] { "db", "init" },
                     new[] { "rates", "fetch", "--from", "2026-11-02", "--to", "2026-11-23" },
                     new[] { "trades", "import", Sample("trades-fictional.csv") },
                     new[] { "yields", "fetch", "--from", "2026-11-02", "--to", "2026-11-20" },
                     new[] { "bonds", "import", Sample("bonds-fictional.csv") },
                     new[] { "bonds", "trades", "import", Sample("bond-trades-fictional.csv") },
                 })
        {
            Assert.Equal(App.Ok, await App.RunAsync(args, TextWriter.Null, TextWriter.Null, env));
        }

        Assert.Equal(App.Breaks, await App.RunAsync(new[] { "recon", "run", Sample("confirmations-fictional.csv") }, TextWriter.Null, TextWriter.Null, env));

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
        Assert.Contains("Monday 23 November 2026", html);
        Assert.Contains("CAD 42,827.80", html);
        Assert.Contains("+4,117.60", html);
        Assert.Contains("This desk is invented", html);
        Assert.Contains("The Bank of Canada does not endorse this project.", html);
    }

    [Fact]
    public async Task A_day_without_rates_is_shown_as_refused_not_as_an_empty_book()
    {
        var response = await _http.GetAsync("/?date=2026-11-11");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("No book for Wednesday 11 November 2026", html);
        Assert.Contains("published no rates for 2026-11-11", html);
        Assert.DoesNotContain("<table", html);
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

    [Fact]
    public async Task The_ribbon_links_every_published_day_and_marks_the_current_one()
    {
        var html = await _http.GetStringAsync("/?date=2026-11-12");
        Assert.Contains("<figure class=\"ribbon\">", html);
        Assert.Contains("href=\"/?date=2026-11-02\"", html);
        Assert.Contains("href=\"/?date=2026-11-23\"", html);
        Assert.Contains("aria-current=\"date\"", html);
    }

    [Fact]
    public async Task The_bond_page_shows_the_bond_book_and_the_curve()
    {
        var response = await _http.GetAsync("/bonds");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Friday 20 November 2026", html);
        Assert.Contains("+81,890.77", html);
        Assert.Contains("<figure class=\"curve\">", html);
        Assert.Contains("Not market quotes", html);
    }

    [Fact]
    public async Task The_bond_JSON_endpoint_gives_the_same_book_as_the_command_line()
    {
        var stdout = new StringWriter();
        var env = new AppEnvironment(_db.ConnectionString, Path.GetTempPath(), new HttpClient(), TimeProvider.System);
        Assert.Equal(App.Ok, await App.RunAsync(new[] { "bonds", "book", "--date", "2026-11-20", "--json" }, stdout, TextWriter.Null, env));
        Assert.Equal(stdout.ToString().TrimEnd(), (await _http.GetStringAsync("/api/bonds?date=2026-11-20")).TrimEnd());
    }

    [Fact]
    public async Task The_breaks_page_lists_the_latest_run()
    {
        var html = await _http.GetStringAsync("/breaks");
        Assert.Contains("confirmations-fictional.csv", html);
        Assert.Contains("Missing confirmation", html);
        Assert.Contains("FICT-0006", html);
        Assert.Contains("5 of 8 trades matched", html);
    }

    [Fact]
    public async Task The_book_can_be_shown_in_a_second_currency_and_a_bad_code_is_400()
    {
        var html = await _http.GetStringAsync("/?date=2026-11-23&ccy=USD");
        Assert.Contains("In USD", html);
        Assert.Contains("USD 29,805.69", html);
        Assert.Contains("<option value=\"USD\" selected>", html);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.GetAsync("/?ccy=dollars")).StatusCode);
    }

    [Fact]
    public async Task Every_tab_link_on_the_FX_page_opens_a_page()
    {
        // The FX book's latest day (2026-11-23) has no yields yet: a tab must not carry that date to the bond book.
        var html = await _http.GetStringAsync("/?ccy=USD");
        foreach (var href in new[] { "/?ccy=USD", "/bonds?ccy=USD", "/breaks?ccy=USD" })
        {
            Assert.Contains($"href=\"{href}\"", html);
            Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync(href)).StatusCode);
        }
    }

    [Fact]
    public async Task A_currency_without_a_rate_is_a_note_beside_the_book_not_a_refusal()
    {
        var response = await _http.GetAsync("/?date=2026-11-23&ccy=RUB");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Not shown in RUB", html);
        Assert.Contains("+4,117.60", html);
    }
}
