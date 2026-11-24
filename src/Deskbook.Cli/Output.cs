using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deskbook.Cli;

/// <summary>Writes results: JSON for agents and scripts (--json), plain text for a person.</summary>
public static class Output
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Canadian-dollar amounts are summed unrounded and rounded to cents only here, for display.</summary>
    public static decimal Cents(decimal cad) => Math.Round(cad, 2, MidpointRounding.AwayFromZero);

    public static string Amount(decimal value) => value.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
