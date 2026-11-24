using System.Text;

namespace Deskbook.Core.Reporting;

/// <summary>A <see cref="Translation"/> as a JSON block and as text lines, amounts rounded to cents of that currency.</summary>
public static class TranslationReport
{
    public sealed record TranslationJson(string Currency, decimal CadPerUnit, decimal? PreviousCadPerUnit, decimal Value,
        decimal PreviousValue, IReadOnlyDictionary<string, decimal> Pnl, string Note);

    public const string Note =
        "Converted from CAD at the Bank of Canada rate of each day. P&L in this currency = each CAD part at today's rate " +
        "+ translation (the previous value re-marked at the rate change).";

    public static TranslationJson ToJson(Translation t)
    {
        var pnl = new Dictionary<string, decimal>();
        foreach (var (part, amount) in t.Parts) pnl[part] = Output.Cents(amount);
        pnl["translation"] = Output.Cents(t.TranslationEffect);
        pnl["total"] = Output.Cents(t.TotalPnl);
        return new TranslationJson(t.Currency.Code, t.CadPerUnit, t.PreviousCadPerUnit, Output.Cents(t.Value), Output.Cents(t.PreviousValue), pnl, Note);
    }

    public static string ToText(Translation t)
    {
        var text = new StringBuilder();
        var c = t.Currency.Code;
        text.AppendLine($"In {c} (CAD per {c}: {t.CadPerUnit}{(t.PreviousCadPerUnit is null ? "" : $", previous {t.PreviousCadPerUnit}")}):");
        text.AppendLine($"  Book value: {c} {Output.Amount(Output.Cents(t.Value))} (previous {c} {Output.Amount(Output.Cents(t.PreviousValue))})");
        var parts = string.Join(" + ", t.Parts.Select(p => $"{p.Part.Replace('_', ' ')} {Output.Amount(Output.Cents(p.Amount))}"));
        text.AppendLine($"  Day's P&L:  {c} {Output.Amount(Output.Cents(t.TotalPnl))} = {parts} + translation {Output.Amount(Output.Cents(t.TranslationEffect))}");
        return text.ToString();
    }
}
