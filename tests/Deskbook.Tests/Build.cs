using Deskbook.Core;

namespace Deskbook.Tests;

/// <summary>Short ways to build test data. Every trade here is fictional.</summary>
internal static class Build
{
    public static DateOnly D(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd");

    public static RateSet Rates(string date, params (string Currency, decimal CadPerUnit)[] rates) =>
        new(D(date), rates.ToDictionary(r => new Currency(r.Currency), r => r.CadPerUnit));

    public static Trade Trade(string id, string date, string buy, decimal buyAmount, string sell, decimal sellAmount) =>
        new(id, D(date), "Test Counterparty (fictional)", new Leg(new Currency(buy), buyAmount), new Leg(new Currency(sell), sellAmount));

    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "samples", name);
}
