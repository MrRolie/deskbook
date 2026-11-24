using Deskbook.Core;
using Deskbook.Core.Bonds;

namespace Deskbook.Infrastructure.Valet;

/// <summary>A parsed answer from the Valet API: what was asked for, and the days that had rates, oldest first.</summary>
public sealed record ValetObservations(
    string SeriesGroup,
    DateOnly RequestedFrom,
    DateOnly RequestedTo,
    string SourceUrl,
    DateTimeOffset FetchedAt,
    IReadOnlyList<RateSet> Days)
{
    /// <summary>The last day this answer vouches for: its latest day with rates, or null if it had none.</summary>
    public DateOnly? CoveredTo => Days.Count == 0 ? null : Days[^1].Date;
}

/// <summary>A parsed benchmark-yield answer: what was asked for, and the days that had yields, oldest first.</summary>
public sealed record ValetYieldObservations(
    DateOnly RequestedFrom,
    DateOnly RequestedTo,
    string SourceUrl,
    DateTimeOffset FetchedAt,
    IReadOnlyList<YieldCurve> Days)
{
    public DateOnly? CoveredTo => Days.Count == 0 ? null : Days[^1].Date;
}
