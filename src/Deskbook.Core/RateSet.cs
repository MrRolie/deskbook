namespace Deskbook.Core;

/// <summary>
/// The rates published for one day: for each currency, how many Canadian dollars one unit is worth.
/// CAD itself is always 1 and is never stored.
/// </summary>
public sealed class RateSet
{
    private readonly Dictionary<Currency, decimal> _cadPerUnit;

    public DateOnly Date { get; }

    public RateSet(DateOnly date, IReadOnlyDictionary<Currency, decimal> cadPerUnit)
    {
        Date = date;
        _cadPerUnit = new Dictionary<Currency, decimal>(cadPerUnit);
        foreach (var (currency, rate) in _cadPerUnit)
        {
            if (currency == Currency.Cad) throw new ArgumentException("CAD is the reporting currency and has no rate.");
            if (rate <= 0) throw new ArgumentException($"{currency} on {date:yyyy-MM-dd}: a rate must be positive.");
        }
    }

    /// <summary>Every stored rate of the day (CAD excluded), in currency-code order.</summary>
    public IEnumerable<(Currency Currency, decimal CadPerUnit)> All() =>
        _cadPerUnit.OrderBy(p => p.Key.Code, StringComparer.Ordinal).Select(p => (p.Key, p.Value));

    public int Count => _cadPerUnit.Count;

    public bool TryGetCadPerUnit(Currency currency, out decimal rate)
    {
        if (currency == Currency.Cad)
        {
            rate = 1m;
            return true;
        }
        return _cadPerUnit.TryGetValue(currency, out rate);
    }
}
