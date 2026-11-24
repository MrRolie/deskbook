using System.Globalization;
using System.Net;
using Deskbook.Core.Reporting;

namespace Deskbook.Web.Pages;

/// <summary>Small helpers every page uses: escaping, dates in words, signed amounts.</summary>
internal static class Html
{
    public static string E(string text) => WebUtility.HtmlEncode(text);

    /// <summary>"Monday 23 November 2026".</summary>
    public static string LongDate(DateOnly date) => date.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>An amount with its sign always written (a true minus sign), so gain and loss differ without colour.</summary>
    public static string Signed(decimal amount)
    {
        var cents = Output.Cents(amount);
        return (cents > 0 ? "+" : cents < 0 ? "−" : "") + Output.Amount(Math.Abs(cents));
    }

    public static string Plain(decimal amount)
    {
        var cents = Output.Cents(amount);
        return (cents < 0 ? "−" : "") + Output.Amount(Math.Abs(cents));
    }

    public static string Tone(decimal amount) => Output.Cents(amount) switch { > 0 => "gain", < 0 => "loss", _ => "flat" };

    public static string Number(decimal value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
