using System.Globalization;
using Deskbook.Core;

namespace Deskbook.Infrastructure.Valet;

/// <summary>
/// Fetches daily exchange rates from the Bank of Canada's Valet API, one request per call, with no retry loop.
/// Each answer is cached by range. A cached answer is used instead of the network when it already reaches the last
/// day asked for, or when it is less than <see cref="MinimumRefetchInterval"/> old; otherwise the range is asked
/// again. So a fetch made before the day's publication does not hide the rates published later, and running the
/// command repeatedly never turns into a stream of requests. An answer is cached only after it parses.
/// </summary>
public sealed class ValetClient
{
    public const string DailyFxGroup = "FX_RATES_DAILY";
    public static readonly TimeSpan MinimumRefetchInterval = TimeSpan.FromMinutes(15);
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
        var bodyPath = Path.Combine(_cacheDirectory, $"{DailyFxGroup}_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.json");
        var stampPath = bodyPath + ".fetched-at";

        if (File.Exists(bodyPath) && File.Exists(stampPath))
        {
            var cachedAt = DateTimeOffset.Parse(await File.ReadAllTextAsync(stampPath, cancellationToken), CultureInfo.InvariantCulture);
            var cachedDays = InRange(ValetParser.ParseDays(await File.ReadAllTextAsync(bodyPath, cancellationToken)), from, to);
            var reachesTo = cachedDays.Count > 0 && cachedDays[^1].Date == to;
            if (reachesTo || now - cachedAt < MinimumRefetchInterval)
            {
                return (new ValetObservations(DailyFxGroup, from, to, url, cachedAt, cachedDays), true);
            }
        }

        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Valet answered {(int)response.StatusCode} {response.ReasonPhrase} for {url}; " +
                "wait before trying again (see docs/runbook.md)");
        }
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var days = InRange(ValetParser.ParseDays(json), from, to); // throws on a malformed body, before anything is cached

        Directory.CreateDirectory(_cacheDirectory);
        await File.WriteAllTextAsync(bodyPath, json, cancellationToken);
        await File.WriteAllTextAsync(stampPath, now.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
        return (new ValetObservations(DailyFxGroup, from, to, url, now, days), false);
    }

    private static List<RateSet> InRange(IEnumerable<RateSet> days, DateOnly from, DateOnly to) =>
        days.Where(d => d.Date >= from && d.Date <= to).ToList();
}
