using System.Globalization;

namespace Deskbook.Infrastructure.Valet;

/// <summary>
/// Fetches daily exchange rates from the Bank of Canada's Valet API, one request per call, with no retry loop.
/// Every answer is saved to the cache folder, named by range and by the UTC day it was fetched; asking again the
/// same day reads the file instead of the network, as the Bank's guide asks for data that changes once a day.
/// </summary>
public sealed class ValetClient
{
    public const string DailyFxGroup = "FX_RATES_DAILY";
    private const string BaseUrl = "https://www.bankofcanada.ca/valet";

    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private readonly TimeProvider _clock;

    public ValetClient(HttpClient http, string cacheDirectory, TimeProvider clock)
    {
        _http = http;
        _cacheDirectory = cacheDirectory;
        _clock = clock;
    }

    public static string UrlFor(DateOnly from, DateOnly to) =>
        $"{BaseUrl}/observations/group/{DailyFxGroup}/json" +
        $"?start_date={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
        $"&end_date={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    /// <returns>The parsed answer, and whether it came from the cache.</returns>
    public async Task<(ValetObservations Observations, bool FromCache)> FetchDailyFxAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from) throw new ArgumentException("The end date is before the start date.");

        var url = UrlFor(from, to);
        var now = _clock.GetUtcNow();
        var path = Path.Combine(_cacheDirectory,
            $"{DailyFxGroup}_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}_fetched-{now:yyyy-MM-dd}.json");

        string json;
        DateTimeOffset fetchedAt;
        var fromCache = File.Exists(path);
        if (fromCache)
        {
            json = await File.ReadAllTextAsync(path, cancellationToken);
            fetchedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        else
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Valet answered {(int)response.StatusCode} {response.ReasonPhrase} for {url}; " +
                    "wait before trying again (see docs/runbook.md)");
            }
            json = await response.Content.ReadAsStringAsync(cancellationToken);
            Directory.CreateDirectory(_cacheDirectory);
            await File.WriteAllTextAsync(path, json, cancellationToken);
            fetchedAt = now;
        }

        var days = ValetParser.ParseDays(json).Where(d => d.Date >= from && d.Date <= to).ToList();
        return (new ValetObservations(DailyFxGroup, from, to, url, fetchedAt, days), fromCache);
    }
}
