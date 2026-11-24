using System.Globalization;
using System.Text;
using Deskbook.Core;
using Deskbook.Core.Bonds;
using Deskbook.Core.Reconciliation;
using Deskbook.Core.Reporting;
using Deskbook.Infrastructure.Postgres;

namespace Deskbook.Web.Pages;

/// <summary>Where a sheet's links point: its path, and the currency to carry along.</summary>
internal sealed record Nav(string Path, string? Currency)
{
    public string To(DateOnly date) =>
        $"{Path}?date={Output.Date(date)}{(Currency is null ? "" : "&amp;ccy=" + Currency)}";
}

/// <summary>The body of each sheet. Every value that comes from data goes through <see cref="Html.E"/>.</summary>
internal static class Views
{
    public static string FxBook(Book book, IReadOnlyList<PnlDay> history, Translation? translation, Nav nav, RateCalendar calendar,
        IReadOnlyList<string> currencies, string note = "")
    {
        var body = new StringBuilder(Toolbar("FX book", book.Date, calendar, nav, currencies, $"/api/book?date={Output.Date(book.Date)}"));
        body.Append("<div class=\"grid\"><div class=\"stack\">");

        body.Append($"<section class=\"pane\"><h2>Positions <small>valued at Bank of Canada rates of {Html.ShortDate(book.Date)}" +
                    (book.PreviousDate is null ? "" : $", against {Html.ShortDate(book.PreviousDate.Value)}") + "</small></h2><div class=\"scroll\"><table>" +
                    "<thead><tr><th scope=\"col\">Currency</th><th scope=\"col\">Position</th><th scope=\"col\">CAD per unit</th>" +
                    "<th scope=\"col\">Previous</th><th scope=\"col\">Value, CAD</th><th scope=\"col\">Revaluation</th>" +
                    "<th scope=\"col\">New trades</th><th scope=\"col\">Day's P&amp;L</th></tr></thead><tbody>");
        foreach (var p in book.Positions)
        {
            body.Append("<tr>")
                .Append($"<th scope=\"row\">{Html.E(p.Currency.Code)}</th>")
                .Append($"<td>{Html.Acct(p.ClosingAmount)}</td>")
                .Append($"<td>{p.Rate.ToString(CultureInfo.InvariantCulture)}</td>")
                .Append($"<td>{(p.PreviousRate is null ? "" : p.PreviousRate.Value.ToString(CultureInfo.InvariantCulture))}</td>")
                .Append($"<td>{Html.Acct(p.ValueCad)}</td>")
                .Append(Html.PnlCell(p.RevaluationPnlCad))
                .Append(Html.PnlCell(p.NewTradesPnlCad))
                .Append(Html.PnlCell(p.RevaluationPnlCad + p.NewTradesPnlCad))
                .Append("</tr>");
        }
        body.Append("</tbody><tfoot>")
            .Append($"<tr><th scope=\"row\">Book value</th><td></td><td></td><td></td><td>{Html.Acct(book.ValueCad)}</td>" +
                    $"{Html.PnlCell(book.RevaluationPnlCad)}{Html.PnlCell(book.NewTradesPnlCad)}{Html.PnlCell(book.TotalPnlCad)}</tr>")
            .Append(book.PreviousDate is null ? "" :
                    $"<tr><th scope=\"row\" class=\"label\">Less book value of {Html.ShortDate(book.PreviousDate.Value)}</th><td></td><td></td><td></td>" +
                    $"<td class=\"label\">{Html.Acct(book.PreviousValueCad)}</td><td></td><td></td><td></td></tr>")
            .Append($"<tr class=\"result\"><th scope=\"row\">Day's P&amp;L</th><td></td><td></td><td></td>{Html.PnlCell(book.TotalPnlCad, "sum")}<td></td><td></td><td></td></tr>")
            .Append("</tfoot></table></div>");
        body.Append($"<p>New trades: {(book.NewTradeIds.Count == 0 ? "none" : Html.E(string.Join(", ", book.NewTradeIds)))}.</p></section>");

        if (translation is not null) body.Append(Translated(translation));
        body.Append(note);
        body.Append("</div>").Append(History.Pane(history, book.Date, nav.To)).Append("</div>");
        return body.ToString();
    }

    public static string BondBook(BondBook book, IReadOnlyList<PnlDay> history, Translation? translation, Nav nav, RateCalendar calendar,
        IReadOnlyList<string> currencies, YieldCurve today, YieldCurve? before, string note = "")
    {
        var body = new StringBuilder(Toolbar("Bond book", book.Date, calendar, nav, currencies, $"/api/bonds?date={Output.Date(book.Date)}"));
        body.Append("<div class=\"grid\"><div class=\"stack\">");

        body.Append($"<section class=\"pane\"><h2>Bonds <small>model prices from the benchmark curve of {Html.ShortDate(book.Date)}, not market quotes</small></h2>" +
                    "<div class=\"scroll\"><table><thead><tr><th scope=\"col\">Bond</th><th scope=\"col\">Face</th><th scope=\"col\">Yield</th>" +
                    "<th scope=\"col\">Clean</th><th scope=\"col\">Accrued</th><th scope=\"col\">Value, CAD</th><th scope=\"col\">DV01</th>" +
                    "<th scope=\"col\">Held</th><th scope=\"col\">New trades</th><th scope=\"col\">Day's P&amp;L</th></tr></thead><tbody>");
        foreach (var l in book.Lines)
        {
            body.Append("<tr>")
                .Append($"<th scope=\"row\">{Html.E(l.Bond.Id)}<small>{Html.E(l.Bond.Description)}</small></th>")
                .Append($"<td>{Html.Acct(l.ClosingFace)}</td>")
                .Append($"<td>{(l.Price is null ? "" : Html.Number(l.Price.YieldPercent, "0.0000") + "%")}</td>")
                .Append($"<td>{(l.Price is null ? "" : Html.Number(l.Price.CleanPrice, "0.0000"))}</td>")
                .Append($"<td>{(l.Price is null ? "" : Html.Number(l.Price.Accrued, "0.0000"))}</td>")
                .Append($"<td>{Html.Acct(l.MarketValueCad)}</td>")
                .Append($"<td>{Html.Acct(l.Dv01Cad)}</td>")
                .Append(Html.PnlCell(l.HeldPnlCad))
                .Append(Html.PnlCell(l.NewTradesPnlCad))
                .Append(Html.PnlCell(l.HeldPnlCad + l.NewTradesPnlCad))
                .Append("</tr>");
        }
        const string gap = "<td></td><td></td><td></td><td></td>";
        body.Append("</tbody><tfoot>")
            .Append($"<tr><th scope=\"row\">Bonds at market</th>{gap}<td>{Html.Acct(book.MarketValueCad)}</td><td>{Html.Acct(book.Dv01Cad)}</td>" +
                    $"{Html.PnlCell(book.HeldPnlCad)}{Html.PnlCell(book.NewTradesPnlCad)}{Html.PnlCell(book.TotalPnlCad)}</tr>")
            .Append($"<tr><th scope=\"row\" class=\"label\">Cash from bond trades</th>{gap}<td class=\"label\">{Html.Acct(book.CashCad)}</td><td></td><td></td><td></td><td></td></tr>")
            .Append($"<tr class=\"result\"><th scope=\"row\">Book value</th>{gap}<td>{Html.Acct(book.ValueCad)}</td><td></td><td></td><td></td><td></td></tr>")
            .Append(book.PreviousDate is null ? "" :
                    $"<tr><th scope=\"row\" class=\"label\">Less book value of {Html.ShortDate(book.PreviousDate.Value)}</th>{gap}<td class=\"label\">{Html.Acct(book.PreviousValueCad)}</td><td></td><td></td><td></td><td></td></tr>")
            .Append($"<tr class=\"result\"><th scope=\"row\">Day's P&amp;L</th>{gap}{Html.PnlCell(book.TotalPnlCad, "sum")}<td></td><td></td><td></td><td></td></tr>")
            .Append("</tfoot></table></div></section>");

        if (translation is not null) body.Append(Translated(translation));
        body.Append(note);
        body.Append("</div><div class=\"stack\">").Append(Curve(today, before)).Append(History.Pane(history, book.Date, nav.To)).Append("</div></div>");
        return body.ToString();
    }

    public static string Breaks(ReconRun? run, IReadOnlyList<StoredBreak> breaks)
    {
        var body = new StringBuilder("<div class=\"toolbar\"><h1>Breaks</h1>");
        if (run is null)
        {
            body.Append("</div><section class=\"pane\"><h2>No reconciliation yet</h2><p>Run <code>deskbook recon run &lt;confirmations.csv&gt;</code> " +
                        "to compare the desk's trades with the counterparties' confirmations.</p></section>");
            return body.ToString();
        }

        var open = breaks.Count(b => !b.Resolved);
        body.Append($"<span>Run #{run.Id}, {Html.ShortDate(DateOnly.FromDateTime(run.RunAt.UtcDateTime))} {run.RunAt.UtcDateTime:HH:mm} UTC, " +
                    $"{Html.E(run.SourceName)} <span class=\"step\" title=\"SHA-256 {Html.E(run.SourceSha256)}\">{Html.E(run.SourceSha256[..12])}</span></span>" +
                    $"<span class=\"step\">{run.Matched} of {run.TradesCompared} trades matched, {breaks.Count} breaks, {open} open</span></div>");

        if (breaks.Count == 0)
        {
            body.Append("<section class=\"pane\"><h2>No breaks</h2><p>Every trade agrees with its confirmation.</p></section>");
            return body.ToString();
        }

        body.Append("<section class=\"pane\"><h2>Breaks <small>kept as found; a resolution is a note added with <code>deskbook recon resolve</code></small></h2>" +
                    "<div class=\"scroll\"><table class=\"text\"><thead><tr><th scope=\"col\">#</th><th scope=\"col\">Status</th><th scope=\"col\">Kind</th>" +
                    "<th scope=\"col\">Trade</th><th scope=\"col\">Confirmation</th><th scope=\"col\">What differs</th><th scope=\"col\">Resolution</th>" +
                    "</tr></thead><tbody>");
        foreach (var b in breaks)
        {
            var last = b.Resolved ? b.Resolutions[^1] : null;
            body.Append("<tr>")
                .Append($"<th scope=\"row\">{b.Id}</th>")
                .Append(b.Resolved ? "<td><span class=\"status resolved\">Resolved</span></td>" : "<td><span class=\"status open\">Open</span></td>")
                .Append($"<td>{Kind(b.Break.Kind)}</td>")
                .Append($"<td>{Html.E(b.Break.TradeId ?? "")}</td>")
                .Append($"<td>{Html.E(b.Break.ConfirmationId ?? "")}</td>")
                .Append($"<td>{Html.E(b.Break.Detail)}</td>")
                .Append($"<td>{(last is null ? "" : $"{Html.E(last.Note)}<small>{Html.E(last.ResolvedBy)}</small>")}</td>")
                .Append("</tr>");
        }
        body.Append("</tbody></table></div></section>");
        return body.ToString();
    }

    public static string Refused(string sheet, DateOnly? date, IReadOnlyList<string> reasons, IReadOnlyList<PnlDay> history, Nav nav,
        RateCalendar calendar, IReadOnlyList<string> currencies)
    {
        var body = new StringBuilder();
        if (date is not null) body.Append(Toolbar(sheet, date.Value, calendar, nav, currencies, null));
        body.Append("<div class=\"grid\"><div class=\"stack\">")
            .Append($"<section class=\"pane stop\"><h2>No book for {(date is null ? "this day" : Html.LongDate(date.Value))}</h2>" +
                    "<p>The stored data cannot support an honest book, so none is shown:</p><ul>");
        foreach (var reason in reasons) body.Append("<li>").Append(Html.E(reason)).Append("</li>");
        body.Append("</ul></section></div>");
        if (date is not null) body.Append(History.Pane(history, date.Value, nav.To));
        return body.Append("</div>").ToString();
    }

    public static string Message(string title, string text) =>
        $"<section class=\"pane stop\"><h2>{Html.E(title)}</h2><p>{Html.E(text)}</p></section>";

    /// <summary>
    /// The sheet's first line: its name and day, steps to the neighbouring published days, and a plain GET form
    /// to pick a day and a second currency. No script.
    /// </summary>
    private static string Toolbar(string sheet, DateOnly day, RateCalendar calendar, Nav nav, IReadOnlyList<string> currencies, string? json)
    {
        var previous = calendar.PublishedDates.Where(d => d < day).Select(d => (DateOnly?)d).LastOrDefault();
        var next = calendar.PublishedDates.Where(d => d > day).Select(d => (DateOnly?)d).FirstOrDefault();
        var html = new StringBuilder($"<div class=\"toolbar\"><h1>{Html.E(sheet)}, {Html.LongDate(day)}</h1>");
        if (previous is not null) html.Append($"<a class=\"step\" href=\"{nav.To(previous.Value)}\" rel=\"prev\">Previous: {Html.ShortDate(previous.Value)}</a>");
        if (next is not null) html.Append($"<a class=\"step\" href=\"{nav.To(next.Value)}\" rel=\"next\">Next: {Html.ShortDate(next.Value)}</a>");
        html.Append($"<form method=\"get\" action=\"{nav.Path}\"><label for=\"date\">Day</label>" +
                    $"<input type=\"date\" id=\"date\" name=\"date\" value=\"{Output.Date(day)}\">");
        if (currencies.Count > 0)
        {
            var options = string.Concat(currencies.Select(c =>
                $"<option value=\"{(c == "CAD" ? "" : c)}\"{((nav.Currency ?? "CAD") == c ? " selected" : "")}>{c}</option>"));
            html.Append($"<label for=\"ccy\">Also in</label><select id=\"ccy\" name=\"ccy\">{options}</select>");
        }
        html.Append("<button type=\"submit\">Show</button></form>");
        if (json is not null) html.Append($"<a class=\"json\" href=\"{json}\">JSON</a>");
        return html.Append("</div>").ToString();
    }

    private static string Translated(Translation t)
    {
        var c = Html.E(t.Currency.Code);
        var rows = new StringBuilder();
        foreach (var (part, amount) in t.Parts.Select(p => (p.Part, p.Amount)))
        {
            var label = part switch { "new_trades" => "New trades", "revaluation" => "Revaluation", "held" => "Held", _ => part };
            rows.Append($"<tr><th scope=\"row\">{Html.E(label)}</th>{Html.PnlCell(amount)}</tr>");
        }
        return $"<section class=\"pane\"><h2>In {c} <small>at {t.CadPerUnit.ToString(CultureInfo.InvariantCulture)} CAD per {c}" +
               (t.PreviousCadPerUnit is null ? "" : $", previously {t.PreviousCadPerUnit.Value.ToString(CultureInfo.InvariantCulture)}") +
               "; translation re-marks the previous value at the rate change</small></h2><div class=\"scroll\"><table class=\"narrow\">" +
               $"<thead><tr><th scope=\"col\"></th><th scope=\"col\">{c}</th></tr></thead><tbody>" +
               $"<tr><th scope=\"row\">Book value</th><td>{Html.Acct(t.Value)}</td></tr>" +
               $"<tr><th scope=\"row\">Previous book value</th><td>{Html.Acct(t.PreviousValue)}</td></tr>" +
               rows +
               $"<tr><th scope=\"row\">Translation</th>{Html.PnlCell(t.TranslationEffect)}</tr></tbody>" +
               $"<tfoot><tr class=\"result\"><th scope=\"row\">Day's P&amp;L</th>{Html.PnlCell(t.TotalPnl, "sum")}</tr></tfoot></table></div></section>";
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
        const int W = 300, H = 170, L = 36, R = 12, T = 18, B = 24;
        var all = today.Points.Values.Concat(before?.Points.Values ?? Enumerable.Empty<decimal>()).ToList();
        if (all.Count == 0) return "";
        var lo = Math.Floor(all.Min() * 10 - 1) / 10;
        var hi = Math.Ceiling(all.Max() * 10 + 1) / 10;
        double X(int tenor) => L + (tenor - 2) / 8.0 * (W - L - R);
        double Y(decimal y) => T + (double)((hi - y) / (hi - lo)) * (H - T - B);
        string Line(YieldCurve c) => string.Join(" ", c.Points.Select(p => $"{X(p.Key):0.#},{Y(p.Value):0.#}"));

        var svg = new StringBuilder($"<section class=\"pane curve\"><h2>Benchmark yields, % <small>dashed: {(before is null ? "none" : Html.ShortDate(before.Date))}</small></h2>");
        svg.Append($"<svg viewBox=\"0 0 {W} {H}\" role=\"img\" aria-labelledby=\"curve-title\"><title id=\"curve-title\">Bank of Canada benchmark yields on " +
                   $"{Html.LongDate(today.Date)}{(before is null ? "" : $", with {Html.LongDate(before.Date)} dashed")}</title>");
        for (var g = lo; g <= hi; g += 0.1m)
        {
            if (Math.Round(g * 10) % 2 != 0) continue;
            svg.Append($"<line class=\"grid\" x1=\"{L}\" x2=\"{W - R}\" y1=\"{Y(g):0.#}\" y2=\"{Y(g):0.#}\"/>");
            svg.Append($"<text x=\"{L - 5}\" y=\"{Y(g) + 4:0.#}\" text-anchor=\"end\">{g:0.0}</text>");
        }
        svg.Append($"<line class=\"axis\" x1=\"{L}\" x2=\"{W - R}\" y1=\"{H - B}\" y2=\"{H - B}\"/>");
        foreach (var tenor in YieldCurve.Tenors)
        {
            svg.Append($"<text x=\"{X(tenor):0.#}\" y=\"{H - 8}\" text-anchor=\"middle\">{tenor}y</text>");
        }
        if (before is not null) svg.Append($"<polyline class=\"before\" points=\"{Line(before)}\"/>");
        svg.Append($"<polyline class=\"today\" points=\"{Line(today)}\"/>");
        foreach (var (tenor, y) in today.Points)
        {
            svg.Append($"<rect class=\"pt\" x=\"{X(tenor) - 2.5:0.#}\" y=\"{Y(y) - 2.5:0.#}\" width=\"5\" height=\"5\"/>");
            var anchor = tenor == YieldCurve.Tenors[0] ? " style=\"text-anchor:start\"" : tenor == YieldCurve.Tenors[^1] ? " style=\"text-anchor:end\"" : "";
            svg.Append($"<text class=\"val\" x=\"{X(tenor):0.#}\" y=\"{Y(y) - 7:0.#}\"{anchor}>{y:0.00}</text>");
        }
        return svg.Append("</svg></section>").ToString();
    }
}
