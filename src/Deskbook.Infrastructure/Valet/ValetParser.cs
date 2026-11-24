using System.Globalization;
using System.Text.Json;
using Deskbook.Core;
using Deskbook.Core.Bonds;

namespace Deskbook.Infrastructure.Valet;

/// <summary>
/// Reads the JSON of a Valet "observations" answer. Each observation is one day: a "d" date and one entry per
/// series, such as "FXUSDCAD": {"v": "1.4369"}. Series that are not FX&lt;currency&gt;CAD are ignored.
/// </summary>
public static class ValetParser
{
    public static IReadOnlyList<RateSet> ParseDays(string json)
    {
        using var document = JsonDocument.Parse(json);
        var days = new List<RateSet>();

        foreach (var observation in document.RootElement.GetProperty("observations").EnumerateArray())
        {
            var date = DateOnly.ParseExact(observation.GetProperty("d").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var rates = new Dictionary<Currency, decimal>();

            foreach (var series in observation.EnumerateObject())
            {
                if (!IsFxToCad(series.Name)) continue;
                if (!series.Value.TryGetProperty("v", out var value) || value.ValueKind != JsonValueKind.String) continue;
                var text = value.GetString();
                if (string.IsNullOrWhiteSpace(text)) continue;

                rates[new Currency(series.Name.Substring(2, 3))] =
                    decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            }

            if (rates.Count > 0) days.Add(new RateSet(date, rates));
        }

        // The API's order depends on the query; never rely on it.
        return days.OrderBy(d => d.Date).ToList();
    }

    /// <summary>
    /// Reads a "bond_yields_benchmark" answer: series BD.CDN.2YR.DQ.YLD to BD.CDN.10YR.DQ.YLD, yields in percent.
    /// The long-term and real-return series are ignored (their terms are not stated as a number of years).
    /// </summary>
    public static IReadOnlyList<YieldCurve> ParseYieldDays(string json)
    {
        using var document = JsonDocument.Parse(json);
        var days = new List<YieldCurve>();
        foreach (var observation in document.RootElement.GetProperty("observations").EnumerateArray())
        {
            var date = DateOnly.ParseExact(observation.GetProperty("d").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var points = new Dictionary<int, decimal>();
            foreach (var tenor in YieldCurve.Tenors)
            {
                if (!observation.TryGetProperty($"BD.CDN.{tenor}YR.DQ.YLD", out var series)) continue;
                if (!series.TryGetProperty("v", out var value) || value.ValueKind != JsonValueKind.String) continue;
                var text = value.GetString();
                if (string.IsNullOrWhiteSpace(text)) continue;
                points[tenor] = decimal.Parse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            }
            if (points.Count > 0) days.Add(new YieldCurve(date, points));
        }
        return days.OrderBy(d => d.Date).ToList();
    }

    private static bool IsFxToCad(string name) =>
        name.Length == 8 && name.StartsWith("FX", StringComparison.Ordinal) && name.EndsWith("CAD", StringComparison.Ordinal)
        && name.Substring(2, 3).All(c => c is >= 'A' and <= 'Z');
}
