using Deskbook.Core.Reconciliation;

namespace Deskbook.Core.Reporting;

/// <summary>Reconciliation results as JSON-ready records and as text lines.</summary>
public static class ReconReport
{
    public sealed record BreakJson(long? BreakId, string Kind, string? TradeId, string? ConfirmationId, string? Field,
        string? DeskValue, string? CounterpartyValue, string Detail, bool Resolved, IReadOnlyList<ResolutionJson> Resolutions);

    public sealed record ResolutionJson(string ResolvedAt, string ResolvedBy, string Note);

    public static BreakJson ToJson(long? id, Break b, IReadOnlyList<(DateTimeOffset At, string By, string Note)> resolutions) => new(
        id, b.Kind.ToString(), b.TradeId, b.ConfirmationId, b.Field, b.DeskValue, b.CounterpartyValue, b.Detail,
        resolutions.Count > 0,
        resolutions.Select(r => new ResolutionJson(r.At.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture), r.By, r.Note)).ToList());

    public static string Line(long? id, Break b, bool resolved) =>
        $"{(id is null ? "" : $"#{id} ")}{(resolved ? "[resolved] " : "")}{b.Kind} " +
        $"(trade {b.TradeId ?? "-"}, confirmation {b.ConfirmationId ?? "-"}): {b.Detail}";
}
