using System.Text.Json;
using System.Text.Json.Nodes;
using Deskbook.Cli;
using static Deskbook.IntegrationTests.Files;

namespace Deskbook.IntegrationTests;

/// <summary>
/// When the command line goes back to the Bank and when it reads its cache. The case that matters: a fetch made
/// before the day's rates are published must not hide them for the rest of the day.
/// </summary>
public class ValetCacheTests : IClassFixture<TestDatabase>
{
    private readonly TestDatabase _db;
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 23, 20, 0, 0, TimeSpan.Zero));
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "deskbook-tests", Guid.NewGuid().ToString("N"));

    public ValetCacheTests(TestDatabase db)
    {
        _db = db;
    }

    private async Task<(int Code, JsonElement Json)> Fetch(SavedValetResponse valet)
    {
        var env = new AppEnvironment(_db.ConnectionString, _cache, new HttpClient(valet), _clock);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Assert.Equal(App.Ok, await App.RunAsync(new[] { "db", "init" }, stdout, stderr, env));
        stdout.GetStringBuilder().Clear();

        var code = await App.RunAsync(new[] { "rates", "fetch", "--from", "2026-11-02", "--to", "2026-11-23", "--json" }, stdout, stderr, env);
        var text = code == App.Ok ? stdout.ToString() : stderr.ToString();
        return (code, JsonDocument.Parse(text).RootElement.Clone());
    }

    /// <summary>The saved answer without its last day, as Valet would answer before 2026-11-23's publication.</summary>
    private static string BeforePublication()
    {
        var json = JsonNode.Parse(File.ReadAllText(Fixture("valet-fx-2026-11.json")))!;
        var observations = json["observations"]!.AsArray();
        observations.RemoveAt(observations.Count - 1);
        return json.ToJsonString();
    }

    [Fact]
    public async Task An_answer_that_stops_short_is_asked_again_once_it_is_old_enough()
    {
        var valet = new SavedValetResponse(BeforePublication(), File.ReadAllText(Fixture("valet-fx-2026-11.json")));

        var (_, early) = await Fetch(valet);
        Assert.Equal("2026-11-20", early.GetProperty("covered_to").GetString());

        _clock.Now += TimeSpan.FromHours(1);
        var (_, later) = await Fetch(valet);

        Assert.Equal(2, valet.Calls);
        Assert.False(later.GetProperty("from_cache").GetBoolean());
        Assert.Equal("2026-11-23", later.GetProperty("covered_to").GetString());
    }

    [Fact]
    public async Task An_answer_that_stops_short_is_not_asked_again_within_fifteen_minutes()
    {
        var valet = new SavedValetResponse(BeforePublication(), File.ReadAllText(Fixture("valet-fx-2026-11.json")));

        await Fetch(valet);
        _clock.Now += TimeSpan.FromMinutes(5);
        var (_, again) = await Fetch(valet);

        Assert.Equal(1, valet.Calls);
        Assert.True(again.GetProperty("from_cache").GetBoolean());
    }

    [Fact]
    public async Task A_malformed_answer_is_a_failure_with_exit_code_1_and_is_not_cached()
    {
        var valet = new SavedValetResponse("<html>Service unavailable</html>");

        var (code, error) = await Fetch(valet);

        Assert.Equal(App.Failure, code);
        Assert.Equal("failure", error.GetProperty("error").GetString());
        Assert.False(Directory.Exists(_cache) && Directory.EnumerateFiles(_cache).Any());
    }
}
