namespace Deskbook.Core;

/// <summary>
/// An ISO 4217 currency code such as "USD". A value type: two Currency values with the same code are equal.
/// </summary>
public readonly record struct Currency
{
    /// <summary>The reporting currency: every value and every P&amp;L in the book is in Canadian dollars.</summary>
    public static readonly Currency Cad = new("CAD");

    public string Code { get; }

    public Currency(string code)
    {
        if (code is null || code.Length != 3 || !code.All(c => c is >= 'A' and <= 'Z'))
        {
            throw new ArgumentException($"'{code}' is not a three-letter upper-case currency code.", nameof(code));
        }
        Code = code;
    }

    public override string ToString() => Code;
}
