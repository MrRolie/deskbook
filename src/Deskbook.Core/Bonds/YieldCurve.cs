namespace Deskbook.Core.Bonds;

/// <summary>
/// One day's Government of Canada benchmark yields (Bank of Canada, in percent) at 2, 3, 5, 7 and 10 years, and the
/// yield for any term between them by straight-line interpolation. Outside 2 to 10 years there is no yield: the
/// Bank does not state the term of its "long-term" benchmark, and guessing a curve beyond the data would put a
/// made-up number behind a price.
/// </summary>
public sealed class YieldCurve
{
    public static readonly int[] Tenors = { 2, 3, 5, 7, 10 };

    private readonly SortedDictionary<int, decimal> _yields;

    public DateOnly Date { get; }

    public YieldCurve(DateOnly date, IReadOnlyDictionary<int, decimal> yieldPercentByTenor)
    {
        Date = date;
        _yields = new SortedDictionary<int, decimal>(yieldPercentByTenor.ToDictionary(p => p.Key, p => p.Value));
        foreach (var tenor in _yields.Keys)
        {
            if (!Tenors.Contains(tenor)) throw new ArgumentException($"{tenor} years is not a benchmark tenor.");
        }
    }

    public IReadOnlyDictionary<int, decimal> Points => _yields;

    /// <summary>The term in years from the curve's date to <paramref name="maturity"/>, counting 365.25 days a year.</summary>
    public decimal YearsTo(DateOnly maturity) => (maturity.DayNumber - Date.DayNumber) / 365.25m;

    /// <summary>The interpolated yield in percent, or a refusal reason.</summary>
    public bool TryYieldFor(DateOnly maturity, out decimal yieldPercent, out string? reason)
    {
        yieldPercent = 0;
        var years = YearsTo(maturity);
        if (years < Tenors[0] || years > Tenors[^1])
        {
            reason = $"maturity {maturity:yyyy-MM-dd} is {years:0.00} years away, outside the benchmark curve (2 to 10 years)";
            return false;
        }
        var below = Tenors.Last(t => t <= years);
        var above = Tenors.First(t => t >= years);
        if (!_yields.TryGetValue(below, out var yBelow) || !_yields.TryGetValue(above, out var yAbove))
        {
            reason = $"no {below}- or {above}-year benchmark yield published for {Date:yyyy-MM-dd}";
            return false;
        }
        yieldPercent = below == above ? yBelow : yBelow + (yAbove - yBelow) * (years - below) / (above - below);
        reason = null;
        return true;
    }
}
