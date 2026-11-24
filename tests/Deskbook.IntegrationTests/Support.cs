using System.Net;
using Deskbook.Core;

namespace Deskbook.IntegrationTests;

internal static class Files
{
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    public static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "samples", name);
    public static DateOnly D(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd");

    public static Trade Trade(string id, string date, string buy, decimal buyAmount, string sell, decimal sellAmount) =>
        new(id, D(date), "Test Counterparty (fictional)", new Leg(new Currency(buy), buyAmount), new Leg(new Currency(sell), sellAmount));
}

/// <summary>
/// Stands in for the Bank of Canada: answers each request with the next saved body (the last one repeats) and
/// counts calls.
/// </summary>
internal sealed class SavedValetResponse(params string[] bodies) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public List<string> Urls { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = bodies[Math.Min(Calls, bodies.Length - 1)];
        Calls++;
        Urls.Add(request.RequestUri!.ToString());
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}

/// <summary>A clock that only moves when a test moves it, so "today" and the cache's age are fixed in tests.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Stands in for the Bank of Canada when it is down: every request gets 503.</summary>
internal sealed class UnavailableValet : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
}
