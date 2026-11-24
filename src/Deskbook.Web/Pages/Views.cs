using System.Globalization;
using System.Text;
using Deskbook.Core;
using Deskbook.Core.Bonds;
using Deskbook.Core.Reconciliation;
using Deskbook.Core.Reporting;
using Deskbook.Infrastructure.Postgres;

namespace Deskbook.Web.Pages;

/// <summary>Where a view's links point: its path, and the currency to carry along.</summary>
internal sealed record Nav(string Path, string? Currency)
{
    public string To(DateOnly date) =>
        $"{Path}?date={Output.Date(date)}{(Currency is null ? "" : "&amp;ccy=" + Currency)}";
}

/// <summary>The body of each view. Every value that comes from data goes through <see cref="Html.E"/>.</summary>
internal static class Views
{
    public static string FxBook(Book book, IReadOnlyList<RibbonDay> ribbon, Translation? translation, Nav nav, RateCalendar calendar)
    {
        var body = new StringBuilder();
        body.Append(DayHead(book.Date, book.PreviousDate, calendar, nav, book.TotalPnlCad,
            $"New trades {Html.Signed(book.NewTradesPnlCad)} and revaluation {Html.Signed(book.RevaluationPnlCad)}"));
        body.Append(Ribbon.Render(ribbon, book.Date, nav.To, "CAD"));

        body.Append("<ul class=\"facts\">")
            .Append(Fact("Book value", "CAD " + Output.Amount(Output.Cents(book.ValueCad))))
            .Append(Fact("Previous value", "CAD " + Html.Plain(book.PreviousValueCad)))
            .Append(Fact("New trades", book.NewTradeIds.Count == 0 ? "none" : Html.E(string.Join(", ", book.NewTradeIds))))
            .Append("</ul>");

        body.Append("<h2>Positions</h2><div class=\"sheet\"><table><thead><tr><th scope=\"col\">Currency</th><th scope=\"col\">Position</th>" +
                    "<th scope=\"col\">CAD per unit</th><th scope=\"col\">Previous</th><th scope=\"col\">Value, CAD</th>" +
                    "<th scope=\"col\">Revaluation</th><th scope=\"col\">New trades</th></tr></thead><tbody>");
        foreach (var p in book.Positions)
        {
            body.Append("<tr>")
                .Append($"<td class=\"name\">{Html.E(p.Currency.Code)}</td>")
                .Append($"<td>{Html.Plain(p.ClosingAmount)}</td>")
                .Append($"<td>{p.Rate.ToString(CultureInfo.InvariantCulture)}</td>")
                .Append($"<td>{(p.PreviousRate is null ? "" : p.PreviousRate.Value.ToString(CultureInfo.InvariantCulture))}</td>")
                .Append($"<td>{Html.Plain(p.ValueCad)}</td>")
                .Append($"<td class=\"{Html.Tone(p.RevaluationPnlCad)}\">{Html.Signed(p.RevaluationPnlCad)}</td>")
                .Append($"<td class=\"{Html.Tone(p.NewTradesPnlCad)}\">{Html.Signed(p.NewTradesPnlCad)}</td>")
                .Append("</tr>");
        }
        body.Append($"</tbody><tfoot><tr><td>Total</td><td></td><td></td><td></td><td>{Html.Plain(book.ValueCad)}</td>" +
                    $"<td class=\"{Html.Tone(book.RevaluationPnlCad)}\">{Html.Signed(book.RevaluationPnlCad)}</td>" +
                    $"<td class=\"{Html.Tone(book.NewTradesPnlCad)}\">{Html.Signed(book.NewTradesPnlCad)}</td></tr></tfoot></table></div>");
        body.Append("<p class=\"note\">A new trade's line shows the value of both its legs at the day's rate; across currencies they add up to " +
                    "what the trade made. Amounts are summed unrounded and shown to the cent. " +
                    $"The same book as JSON: <a href=\"/api/book?date={Output.Date(book.Date)}\">/api/book?date={Output.Date(book.Date)}</a>.</p>");

        if (translation is not null) body.Append(Translated(translation, "new trades", "revaluation"));
        return body.ToString();
    }

    public static string BondBook(BondBook book, IReadOnlyList<RibbonDay> ribbon, Translation? translation, Nav nav, RateCalendar calendar,
        YieldCurve today, YieldCurve? before)
    {
        var body = new StringBuilder();
        body.Append(DayHead(book.Date, book.PreviousDate, calendar, nav, book.TotalPnlCad,
            $"Held positions {Html.Signed(book.HeldPnlCad)} and new trades {Html.Signed(book.NewTradesPnlCad)}"));
        body.Append(Ribbon.Render(ribbon, book.Date, nav.To, "CAD"));

        body.Append("<ul class=\"facts\">")
            .Append(Fact("Book value", "CAD " + Html.Plain(book.ValueCad)))
            .Append(Fact("Bonds at market", "CAD " + Html.Plain(book.MarketValueCad)))
            .Append(Fact("Cash from bond trades", "CAD " + Html.Plain(book.CashCad)))
            .Append(Fact("DV01", "CAD " + Html.Plain(book.Dv01Cad)))
            .Append("</ul>");

        body.Append("<h2>Benchmark curve</h2>").Append(Curve(today, before));

        body.Append("<h2>Bonds</h2><div class=\"sheet\"><table><thead><tr><th scope=\"col\">Bond</th><th scope=\"col\">Face</th>" +
                    "<th scope=\"col\">Yield</th><th scope=\"col\">Clean</th><th scope=\"col\">Accrued</th><th scope=\"col\">Value, CAD</th>" +
                    "<th scope=\"col\">DV01</th><th scope=\"col\">Held</th><th scope=\"col\">New trades</th></tr></thead><tbody>");
        foreach (var l in book.Lines)
        {
            body.Append("<tr>")
                .Append($"<td class=\"name\">{Html.E(l.Bond.Id)}<small>{Html.E(l.Bond.Description)}</small></td>")
                .Append($"<td>{Html.Plain(l.ClosingFace)}</td>")
                .Append($"<td>{(l.Price is null ? "" : Html.Number(l.Price.YieldPercent, "0.0000") + "%")}</td>")
                .Append($"<td>{(l.Price is null ? "" : Html.Number(l.Price.CleanPrice, "0.0000"))}</td>")
                .Append($"<td>{(l.Price is null ? "" : Html.Number(l.Price.Accrued, "0.0000"))}</td>")
                .Append($"<td>{Html.Plain(l.MarketValueCad)}</td>")
                .Append($"<td>{Html.Plain(l.Dv01Cad)}</td>")
                .Append($"<td class=\"{Html.Tone(l.HeldPnlCad)}\">{Html.Signed(l.HeldPnlCad)}</td>")
                .Append($"<td class=\"{Html.Tone(l.NewTradesPnlCad)}\">{Html.Signed(l.NewTradesPnlCad)}</td>")
                .Append("</tr>");
        }
        body.Append($"</tbody><tfoot><tr><td>Total</td><td></td><td></td><td></td><td></td><td>{Html.Plain(book.MarketValueCad)}</td>" +
                    $"<td>{Html.Plain(book.Dv01Cad)}</td><td class=\"{Html.Tone(book.HeldPnlCad)}\">{Html.Signed(book.HeldPnlCad)}</td>" +
                    $"<td class=\"{Html.Tone(book.NewTradesPnlCad)}\">{Html.Signed(book.NewTradesPnlCad)}</td></tr></tfoot></table></div>");
        body.Append($"<p class=\"note\">{Html.E(BondReport.Model)} Prices per 100 of face. " +
                    $"The same book as JSON: <a href=\"/api/bonds?date={Output.Date(book.Date)}\">/api/bonds?date={Output.Date(book.Date)}</a>.</p>");

        if (translation is not null) body.Append(Translated(translation, "held", "new trades"));
        return body.ToString();
    }

    public static string Breaks(ReconRun? run, IReadOnlyList<StoredBreak> breaks)
    {
        var body = new StringBuilder("<div class=\"day-head\"><div><h1>Breaks</h1>");
        if (run is null)
        {
            body.Append("<p class=\"since\">No reconciliation has run yet. Run <code>deskbook recon run &lt;confirmations.csv&gt;</code> " +
                        "to compare the desk's trades with the counterparties' confirmations.</p></div></div>");
            return body.ToString();
        }

        var open = breaks.Count(b => !b.Resolved);
        body.Append($"<p class=\"since\">Latest run, #{run.Id}, on {Html.LongDate(DateOnly.FromDateTime(run.RunAt.UtcDateTime))} at " +
                    $"{run.RunAt.UtcDateTime:HH:mm} UTC, from <strong>{Html.E(run.SourceName)}</strong> " +
                    $"(SHA-256 <span title=\"{Html.E(run.SourceSha256)}\">{Html.E(run.SourceSha256[..12])}</span>).</p></div>");
        body.Append($"<div class=\"result\"><p class=\"figure {(open == 0 ? "gain" : "loss")}\">{open}<span class=\"ccy\">open</span></p>" +
                    $"<p class=\"split\">{run.Matched} of {run.TradesCompared} trades matched</p></div></div>");

        if (breaks.Count == 0)
        {
            body.Append("<p>Every trade agrees with its confirmation.</p>");
            return body.ToString();
        }

        body.Append("<h2>What differs</h2><div class=\"sheet\"><table class=\"text\"><thead><tr><th scope=\"col\">Break</th><th scope=\"col\">Kind</th>" +
                    "<th scope=\"col\">Trade</th><th scope=\"col\">Confirmation</th><th scope=\"col\">Detail</th><th scope=\"col\">Status</th>" +
                    "</tr></thead><tbody>");
        foreach (var b in breaks)
        {
            var status = b.Resolved
                ? $"<span class=\"status resolved\">Resolved</span><br><small>{Html.E(b.Resolutions[^1].ResolvedBy)}: {Html.E(b.Resolutions[^1].Note)}</small>"
                : "<span class=\"status open\">Open</span>";
            body.Append("<tr>")
                .Append($"<td>#{b.Id}</td>")
                .Append($"<td class=\"name\">{Kind(b.Break.Kind)}</td>")
                .Append($"<td>{Html.E(b.Break.TradeId ?? "")}</td>")
                .Append($"<td>{Html.E(b.Break.ConfirmationId ?? "")}</td>")
                .Append($"<td>{Html.E(b.Break.Detail)}</td>")
                .Append($"<td>{status}</td>")
                .Append("</tr>");
        }
        body.Append("</tbody></table></div>");
        body.Append("<p class=\"note\">Breaks are kept as found and are never edited. A resolution is a note added with " +
                    "<code>deskbook recon resolve</code>.</p>");
        return body.ToString();
    }

    public static string Refused(DateOnly? date, IReadOnlyList<string> reasons, IReadOnlyList<RibbonDay> ribbon, Nav nav)
    {
        var body = new StringBuilder();
        if (date is not null) body.Append(Ribbon.Render(ribbon, date.Value, nav.To, "CAD"));
        body.Append($"<section class=\"stop\"><h2>No book for {(date is null ? "this day" : Html.LongDate(date.Value))}</h2>" +
                    "<p>The stored data cannot support an honest book, so none is shown:</p><ul>");
        foreach (var reason in reasons) body.Append("<li>").Append(Html.E(reason)).Append("</li>");
        body.Append("</ul></section>");
        return body.ToString();
    }

    public static string Message(string title, string text) =>
        $"<section class=\"stop\"><h2>{Html.E(title)}</h2><p>{Html.E(text)}</p></section>";

    private static string DayHead(DateOnly date, DateOnly? previous, RateCalendar calendar, Nav nav, decimal pnl, string split)
    {
        var next = calendar.PublishedDates.Where(d => d > date).Select(d => (DateOnly?)d).FirstOrDefault();
        var since = previous is null
            ? "First published day stored."
            : $"Since <a href=\"{nav.To(previous.Value)}\">{Html.LongDate(previous.Value)}</a>, the previous published day.";
        if (next is not null) since += $" Next: <a href=\"{nav.To(next.Value)}\">{Html.LongDate(next.Value)}</a>.";
        return $"<div class=\"day-head\"><div><h1>{Html.LongDate(date)}</h1><p class=\"since\">{since}</p></div>" +
               $"<div class=\"result\"><p class=\"figure {Html.Tone(pnl)}\">{Html.Signed(pnl)}<span class=\"ccy\">CAD</span></p>" +
               $"<p class=\"split\">{split}</p></div></div>";
    }

    private static string Fact(string key, string value) => $"<li><span class=\"k\">{Html.E(key)}</span><span class=\"v\">{value}</span></li>";

    private static string Translated(Translation t, string first, string second)
    {
        var c = Html.E(t.Currency.Code);
        var parts = string.Join(", ", t.Parts.Select(p => $"{p.Part.Replace('_', ' ')} {Html.Signed(p.Amount)}"));
        return $"<section class=\"translated\"><h2>In {c}</h2>" +
               $"<p>Book value {c} {Html.Plain(t.Value)}, previous {c} {Html.Plain(t.PreviousValue)}.</p>" +
               $"<p>Day's P&amp;L <strong class=\"{Html.Tone(t.TotalPnl)}\">{c} {Html.Signed(t.TotalPnl)}</strong>: {parts}, " +
               $"and translation {Html.Signed(t.TranslationEffect)}.</p>" +
               $"<p class=\"why\">At {t.CadPerUnit.ToString(CultureInfo.InvariantCulture)} CAD per {c}" +
               (t.PreviousCadPerUnit is null ? "" : $" (previously {t.PreviousCadPerUnit.Value.ToString(CultureInfo.InvariantCulture)})") +
               ". Translation is the previous value re-marked at the rate change.</p></section>";
    }

    private static string Kind(BreakKind kind) => kind switch
    {
        BreakKind.MissingConfirmation => "Missing confirmation",
        BreakKind.UnknownTrade => "Unknown trade",
        BreakKind.DuplicateConfirmation => "Duplicate confirmation",
        _ => "Field differs",
    };

    /// <summary>Today's benchmark curve as a line, the previous day's dashed behind it, each tenor labelled.</summary>
    private static string Curve(YieldCurve today, YieldCurve? before)
    {
        const int W = 520, H = 200, L = 44, R = 16, T = 24, B = 34;
        var all = today.Points.Values.Concat(before?.Points.Values ?? Enumerable.Empty<decimal>()).ToList();
        if (all.Count == 0) return "";
        var lo = Math.Floor(all.Min() * 10 - 1) / 10;
        var hi = Math.Ceiling(all.Max() * 10 + 1) / 10;
        double X(int tenor) => L + (tenor - 2) / 8.0 * (W - L - R);
        double Y(decimal y) => T + (double)((hi - y) / (hi - lo)) * (H - T - B);
        string Line(YieldCurve c) => string.Join(" ", c.Points.Select(p => $"{X(p.Key):0.#},{Y(p.Value):0.#}"));

        var svg = new StringBuilder($"<figure class=\"curve\"><svg viewBox=\"0 0 {W} {H}\" role=\"img\" aria-labelledby=\"curve-title\">");
        svg.Append($"<title id=\"curve-title\">Benchmark yields on {Html.LongDate(today.Date)}" +
                   (before is null ? "" : $", with {Html.LongDate(before.Date)} dashed") + "</title>");
        for (var g = lo; g <= hi; g += 0.1m)
        {
            if (Math.Round(g * 10) % 2 != 0) continue;
            svg.Append($"<line class=\"grid\" x1=\"{L}\" x2=\"{W - R}\" y1=\"{Y(g):0.#}\" y2=\"{Y(g):0.#}\"/>");
            svg.Append($"<text x=\"{L - 6}\" y=\"{Y(g) + 4:0.#}\" text-anchor=\"end\">{g:0.0}%</text>");
        }
        foreach (var tenor in YieldCurve.Tenors)
        {
            svg.Append($"<text x=\"{X(tenor):0.#}\" y=\"{H - 10}\" text-anchor=\"middle\">{tenor} yr</text>");
        }
        if (before is not null) svg.Append($"<polyline class=\"before\" points=\"{Line(before)}\"/>");
        svg.Append($"<polyline class=\"today\" points=\"{Line(today)}\"/>");
        foreach (var (tenor, y) in today.Points)
        {
            svg.Append($"<circle class=\"pt\" cx=\"{X(tenor):0.#}\" cy=\"{Y(y):0.#}\" r=\"4\"/>");
            svg.Append($"<text class=\"val\" x=\"{X(tenor):0.#}\" y=\"{Y(y) - 10:0.#}\">{y:0.00}</text>");
        }
        svg.Append("</svg><figcaption>Bank of Canada benchmark yields, percent. " +
                   (before is null ? "" : $"Dashed: {Html.LongDate(before.Date)}. ") + "Bonds are priced from this curve.</figcaption></figure>");
        return svg.ToString();
    }
}
