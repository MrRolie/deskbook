using System.Globalization;
using System.Net;
using System.Text;
using Deskbook.Core;
using Deskbook.Core.Reporting;

namespace Deskbook.Web;

/// <summary>
/// The book as one HTML page, built as a string: no template engine, no script, no external file. Every value
/// that comes from data goes through <see cref="WebUtility.HtmlEncode(string)"/>, which escapes &lt; &gt; &amp; and quotes.
/// </summary>
public static class BookPage
{
    public static string Render(Book book, RateCalendar calendar)
    {
        var body = new StringBuilder();
        body.Append(Header(book.Date, calendar));

        body.Append("<section class=\"tiles\">");
        body.Append(Tile("Book value", "CAD " + Output.Amount(Output.Cents(book.ValueCad)),
            "previous " + Output.Amount(Output.Cents(book.PreviousValueCad))));
        body.Append(Tile("Day's P&L", Signed(book.TotalPnlCad), "since " + (book.PreviousDate is null ? "inception" : Output.Date(book.PreviousDate.Value)), Tone(book.TotalPnlCad)));
        body.Append(Tile("New trades", Signed(book.NewTradesPnlCad), book.NewTradeIds.Count == 0 ? "none" : string.Join(", ", book.NewTradeIds), Tone(book.NewTradesPnlCad)));
        body.Append(Tile("Revaluation", Signed(book.RevaluationPnlCad), "positions held overnight", Tone(book.RevaluationPnlCad)));
        body.Append("</section>");

        body.Append("<table><thead><tr><th>Ccy</th><th>Position</th><th>CAD per unit</th><th>Previous</th>" +
                    "<th>Value CAD</th><th>Reval P&amp;L</th><th>New trades P&amp;L</th></tr></thead><tbody>");
        foreach (var p in book.Positions)
        {
            body.Append("<tr>")
                .Append(Cell(p.Currency.Code, "ccy"))
                .Append(Cell(Output.Amount(p.ClosingAmount)))
                .Append(Cell(p.Rate.ToString(CultureInfo.InvariantCulture)))
                .Append(Cell(p.PreviousRate?.ToString(CultureInfo.InvariantCulture) ?? "–"))
                .Append(Cell(Output.Amount(Output.Cents(p.ValueCad))))
                .Append(Cell(Signed(p.RevaluationPnlCad), Tone(p.RevaluationPnlCad)))
                .Append(Cell(Signed(p.NewTradesPnlCad), Tone(p.NewTradesPnlCad)))
                .Append("</tr>");
        }
        body.Append("</tbody></table>");
        body.Append("<p class=\"note\">Day's P&amp;L = new trades + revaluation. CAD amounts are summed unrounded and shown to the cent. " +
                    $"JSON: <a href=\"/api/book?date={Output.Date(book.Date)}\">/api/book?date={Output.Date(book.Date)}</a></p>");

        return Page($"Book {Output.Date(book.Date)}", body.ToString());
    }

    public static string Refused(DateOnly? date, IReadOnlyList<string> reasons, RateCalendar calendar)
    {
        var body = new StringBuilder();
        if (date is not null) body.Append(Header(date.Value, calendar));
        body.Append("<section class=\"refused\"><h2>No book for this day</h2><p>The stored data cannot support an honest book, so none is shown:</p><ul>");
        foreach (var reason in reasons) body.Append("<li>").Append(Encode(reason)).Append("</li>");
        body.Append("</ul></section>");
        return Page("Book refused", body.ToString());
    }

    public static string Message(string title, string text) =>
        Page(title, $"<section class=\"refused\"><h2>{Encode(title)}</h2><p>{Encode(text)}</p></section>");

    private static string Header(DateOnly date, RateCalendar calendar)
    {
        var published = calendar.PublishedDates;
        var previous = published.Where(d => d < date).Select(d => (DateOnly?)d).LastOrDefault();
        var next = published.Where(d => d > date).Select(d => (DateOnly?)d).FirstOrDefault();

        string Link(DateOnly? d, string label) => d is null
            ? $"<span class=\"off\">{label}</span>"
            : $"<a href=\"/?date={Output.Date(d.Value)}\">{label}</a>";

        return "<header><p class=\"desk\">" + Encode(Attribution.Desk) + "</p>" +
               $"<h1>Book for {Output.Date(date)}</h1>" +
               $"<nav>{Link(previous, "← previous published day")} · {Link(next, "next published day →")}</nav></header>";
    }

    private static string Tile(string label, string value, string detail, string tone = "") =>
        $"<div class=\"tile\"><p class=\"label\">{Encode(label)}</p><p class=\"value {tone}\">{Encode(value)}</p>" +
        $"<p class=\"detail\">{Encode(detail)}</p></div>";

    private static string Cell(string text, string css = "") => $"<td class=\"{css}\">{Encode(text)}</td>";

    /// <summary>A CAD amount with its sign always written, so a gain and a loss differ without relying on colour.</summary>
    private static string Signed(decimal cad)
    {
        var cents = Output.Cents(cad);
        return (cents > 0 ? "+" : cents < 0 ? "−" : "") + Output.Amount(Math.Abs(cents));
    }

    private static string Tone(decimal cad) => Output.Cents(cad) switch { > 0 => "up", < 0 => "down", _ => "" };

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private static string Page(string title, string body) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{Encode(title)}} · deskbook (fictional desk)</title>
        <style>
          :root { --ink: #1c2321; --muted: #5d6b66; --line: #d9dfdc; --paper: #f7f8f6; --card: #ffffff;
                  --up: #1d6b45; --down: #a33a2b; --accent: #2f5d8a; }
          @media (prefers-color-scheme: dark) {
            :root { --ink: #e6ebe8; --muted: #9aa8a2; --line: #2e3734; --paper: #141917; --card: #1b2120;
                    --up: #6cc496; --down: #f08a7a; --accent: #8ab4e0; } }
          * { box-sizing: border-box; }
          body { margin: 0; background: var(--paper); color: var(--ink);
                 font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
          main { max-width: 64rem; margin: 0 auto; padding: 2rem 1.25rem 3rem; }
          .banner { background: var(--ink); color: var(--paper); font-size: 13px; padding: .4rem 1.25rem; text-align: center; }
          header .desk { margin: 0; color: var(--muted); letter-spacing: .04em; text-transform: uppercase; font-size: 12px; }
          h1 { margin: .2rem 0 .4rem; font-size: 1.7rem; font-weight: 650; }
          nav { color: var(--muted); font-size: 14px; } nav .off { opacity: .45; }
          a { color: var(--accent); }
          .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(13rem, 1fr)); gap: .75rem; margin: 1.5rem 0; }
          .tile { background: var(--card); border: 1px solid var(--line); border-radius: 8px; padding: .8rem 1rem; }
          .tile p { margin: 0; } .label { color: var(--muted); font-size: 13px; }
          .value { font-size: 1.35rem; font-weight: 650; font-variant-numeric: tabular-nums; }
          .detail { color: var(--muted); font-size: 12.5px; overflow-wrap: anywhere; }
          .up { color: var(--up); } .down { color: var(--down); }
          table { width: 100%; border-collapse: collapse; background: var(--card); border: 1px solid var(--line);
                  border-radius: 8px; overflow: hidden; font-variant-numeric: tabular-nums; }
          th, td { padding: .5rem .75rem; text-align: right; border-bottom: 1px solid var(--line); white-space: nowrap; }
          th { color: var(--muted); font-weight: 600; font-size: 13px; }
          th:first-child, td.ccy { text-align: left; font-weight: 650; }
          tbody tr:last-child td { border-bottom: 0; }
          .note, footer { color: var(--muted); font-size: 13px; }
          .refused { background: var(--card); border: 1px solid var(--line); border-left: 4px solid var(--down);
                     border-radius: 8px; padding: .5rem 1.25rem; margin: 1.5rem 0; }
          footer { margin-top: 2rem; border-top: 1px solid var(--line); padding-top: 1rem; }
          @media (max-width: 40rem) { table { display: block; overflow-x: auto; } }
        </style>
        </head>
        <body>
        <div class="banner">Fictional desk · invented trades and positions · real Bank of Canada exchange rates</div>
        <main>
        {{body}}
        <footer>
          <p>{{Encode(Attribution.Fictional)}}</p>
          <p>{{Encode(Attribution.Source)}}</p>
        </footer>
        </main>
        </body>
        </html>
        """;
}
