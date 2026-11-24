namespace Deskbook.Core;

/// <summary>
/// A book's CAD figures shown in another currency, using the Bank of Canada rate of each day (CAD per unit).
/// In that currency the day's P&amp;L has one more part than in CAD: the translation effect, the previous value
/// re-marked at the rate change. Each CAD part is converted at today's rate, so:
/// total = Σ parts / rate today + previous value in CAD × (1 / rate today − 1 / rate before) = value − previous value.
/// </summary>
public sealed record Translation(
    Currency Currency,
    decimal CadPerUnit,
    decimal? PreviousCadPerUnit,
    decimal Value,
    decimal PreviousValue,
    IReadOnlyList<(string Part, decimal Amount)> Parts,
    decimal TranslationEffect)
{
    public decimal TotalPnl => Value - PreviousValue;

    /// <param name="valueCad">The book's value in CAD on <paramref name="today"/>'s date.</param>
    /// <param name="previousValueCad">The book's value in CAD on the previous published day (0 when there is none).</param>
    /// <param name="pnlPartsCad">The parts of the day's CAD P&amp;L; they must add up to valueCad − previousValueCad.</param>
    public static Translation Translate(Currency target, RateSet today, RateSet? previous, decimal valueCad, decimal previousValueCad,
        IReadOnlyList<(string Part, decimal AmountCad)> pnlPartsCad)
    {
        if (target == Currency.Cad) throw new ArgumentException("The book is already in CAD.");
        if (!today.TryGetCadPerUnit(target, out var rate))
        {
            throw new BookRefusedException($"no {target} rate published for {today.Date:yyyy-MM-dd} to show the book in {target}");
        }

        decimal? previousRate = null;
        if (previous is not null)
        {
            if (!previous.TryGetCadPerUnit(target, out var p))
            {
                throw new BookRefusedException($"no {target} rate published for {previous.Date:yyyy-MM-dd} to show the book in {target}");
            }
            previousRate = p;
        }
        else if (previousValueCad != 0)
        {
            throw new BookRefusedException($"no rates for the previous day to show its value in {target}");
        }

        var parts = pnlPartsCad.Select(p => (p.Part, p.AmountCad / rate)).ToList();
        var previousValue = previousRate is null ? 0m : previousValueCad / previousRate.Value;
        var effect = previousRate is null ? 0m : previousValueCad * (1m / rate - 1m / previousRate.Value);
        return new Translation(target, rate, previousRate, valueCad / rate, previousValue, parts, effect);
    }
}
