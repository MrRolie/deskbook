using System.Globalization;
using System.Net;
using Deskbook.Core.Reporting;

namespace Deskbook.Web.Pages;

/// <summary>Small helpers every page uses: escaping, dates, amounts in the accountant's form.</summary>
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

    /// <summary>"Mon 23 Nov", for steppers and the P&amp;L history.</summary>
    public static string ShortDate(DateOnly date) => date.ToString("ddd d MMM", CultureInfo.InvariantCulture);

    /// <summary>The accountant's form: a negative amount in parentheses, "(474,400.00)", so its sign survives any colour.</summary>
    public static string Acct(decimal amount)
    {
        var cents = Output.Cents(amount);
        return cents < 0 ? $"({Output.Amount(-cents)})" : Output.Amount(cents);
    }

    /// <summary>A P&amp;L cell: accountant's form, red when a loss, a dash when nothing moved.</summary>
    public static string PnlCell(decimal amount, string extraClass = "")
    {
        var cents = Output.Cents(amount);
        var cls = ((cents < 0 ? "loss " : "") + extraClass).Trim();
        return $"<td{(cls.Length == 0 ? "" : $" class=\"{cls}\"")}>{(cents == 0 ? "–" : Acct(amount))}</td>";
    }

    public static string Tone(decimal amount) => Output.Cents(amount) switch { > 0 => "gain", < 0 => "loss", _ => "flat" };

    public static string Number(decimal value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
