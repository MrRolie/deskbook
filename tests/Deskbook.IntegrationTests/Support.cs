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

/// <summary>Stands in for the Bank of Canada: answers every request with a saved Valet response and counts calls.</summary>
internal sealed class SavedValetResponse(string json) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public List<string> Urls { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Urls.Add(request.RequestUri!.ToString());
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}

/// <summary>A clock stopped at one instant, so cache file names and "today" are fixed in tests.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
