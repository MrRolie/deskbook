using Deskbook.Core;

namespace Deskbook.Web.Pages;

internal enum Tab { Fx, Bonds, Breaks }

/// <summary>
/// The page shell every view shares: the desk's masthead with its three views, the currency picker, the notices.
/// One stylesheet, inline: the page loads nothing from anywhere else.
/// </summary>
internal static class Layout
{
    /// <param name="query">The current date and currency as a query string, carried across the three views.</param>
    /// <param name="currencies">Currencies the picker offers (CAD is always first); empty hides the picker.</param>
    public static string Page(string title, Tab active, string query, string body, IReadOnlyList<string> currencies, string? currency, string source)
    {
        string TabLink(Tab tab, string href, string label) =>
            $"<a href=\"{href}{query}\"{(tab == active ? " aria-current=\"page\"" : "")}>{label}</a>";

        var picker = "";
        if (currencies.Count > 0)
        {
            var path = active switch { Tab.Bonds => "/bonds", Tab.Breaks => "/breaks", _ => "/" };
            var options = string.Concat(currencies.Select(c =>
                $"<option value=\"{(c == "CAD" ? "" : c)}\"{((currency ?? "CAD") == c ? " selected" : "")}>{c}</option>"));
            var dateField = query.Contains("date=") ? $"<input type=\"hidden\" name=\"date\" value=\"{Html.E(DateFrom(query))}\">" : "";
            picker = $"<form class=\"picker\" method=\"get\" action=\"{path}\">{dateField}" +
                     $"<label for=\"ccy\">Show in</label><select id=\"ccy\" name=\"ccy\">{options}</select>" +
                     "<button type=\"submit\">Show</button></form>";
        }

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <title>{{Html.E(title)}} | deskbook, a fictional desk</title>
            <style>{{Css}}</style>
            </head>
            <body>
            <header class="masthead">
              <div class="inner">
                <p class="brand"><span class="name">deskbook</span> <span class="desk">{{Html.E(Attribution.Desk)}}</span></p>
                <nav aria-label="Views">{{TabLink(Tab.Fx, "/", "FX book")}}{{TabLink(Tab.Bonds, "/bonds", "Bond book")}}{{TabLink(Tab.Breaks, "/breaks", "Breaks")}}</nav>
                {{picker}}
              </div>
            </header>
            <p class="notice">This desk is invented: its counterparties, bonds, trades and positions do not exist. The market data is real.</p>
            <main>
            {{body}}
            </main>
            <footer>
              <p>{{Html.E(Attribution.Fictional)}}</p>
              <p>{{Html.E(source)}}</p>
            </footer>
            </body>
            </html>
            """;
    }

    private static string DateFrom(string query)
    {
        var start = query.IndexOf("date=", StringComparison.Ordinal) + 5;
        var end = query.IndexOf('&', start);
        return end < 0 ? query[start..] : query[start..end];
    }

    private const string Css = """
        :root {
          --ink: #14213d; --muted: #4f5d6b; --paper: #f3f5f4; --sheet: #ffffff; --rule: #c9d3d1;
          --binder: #1f4e5f; --binder-ink: #e8f0ee; --gain: #1e7a4f; --loss: #b3402a; --signal: #d9a21b;
          --serif: "Iowan Old Style", "Charter", "Bitstream Charter", "Palatino Linotype", Palatino, "Book Antiqua", Georgia, serif;
          --sans: "Avenir Next", "Segoe UI", "Helvetica Neue", "Noto Sans", Arial, sans-serif;
        }
        @media (prefers-color-scheme: dark) {
          :root { --ink: #e4ecea; --muted: #9fb0ad; --paper: #0f1a1f; --sheet: #15232a; --rule: #2a3d44;
                  --binder: #173a47; --binder-ink: #dbe8e5; --gain: #5cc490; --loss: #f0866f; --signal: #e8b93a; }
        }
        * { box-sizing: border-box; }
        html { background: var(--paper); color: var(--ink); }
        body { margin: 0; font: 16px/1.5 var(--sans); font-variant-numeric: tabular-nums lining-nums; }
        a { color: inherit; }
        :focus-visible { outline: 3px solid var(--signal); outline-offset: 2px; }

        .masthead { background: var(--binder); color: var(--binder-ink); }
        .masthead .inner { max-width: 76rem; margin: 0 auto; padding: .9rem 1.5rem; display: flex; flex-wrap: wrap; gap: .75rem 2rem; align-items: center; }
        .brand { margin: 0; display: flex; align-items: baseline; gap: .75rem; }
        .brand .name { font: 600 1.45rem/1 var(--serif); letter-spacing: .01em; }
        .brand .desk { font-size: .9rem; opacity: .8; }
        .masthead nav { display: flex; gap: .25rem; }
        .masthead nav a { text-decoration: none; padding: .35rem .8rem; border-radius: 999px; font-size: .95rem; }
        .masthead nav a:hover { background: rgb(255 255 255 / .1); }
        .masthead nav a[aria-current="page"] { background: var(--binder-ink); color: var(--binder); font-weight: 600; }
        .picker { margin-left: auto; display: flex; align-items: center; gap: .5rem; font-size: .9rem; }
        .picker select, .picker button { font: inherit; border-radius: 6px; border: 1px solid rgb(255 255 255 / .35);
                                         background: transparent; color: inherit; padding: .25rem .5rem; }
        .picker option { color: #14213d; }
        .picker button { cursor: pointer; }

        .notice { max-width: 76rem; margin: 0 auto; padding: .6rem 1.5rem; font-size: .88rem; color: var(--muted);
                  border-bottom: 1px solid var(--rule); }
        main { max-width: 76rem; margin: 0 auto; padding: 2rem 1.5rem 3rem; }

        .day-head { display: grid; grid-template-columns: 1fr auto; gap: 1rem 2.5rem; align-items: end; }
        .day-head h1 { font: 500 clamp(1.8rem, 4vw, 2.6rem)/1.1 var(--serif); margin: 0; letter-spacing: -.01em; }
        .day-head .since { margin: .4rem 0 0; color: var(--muted); }
        .day-head .since a { color: var(--ink); }
        .result { text-align: right; }
        .result .figure { font: 600 clamp(2rem, 5vw, 3.2rem)/1 var(--sans); margin: 0; }
        .result .figure .ccy { font-size: .45em; font-weight: 500; color: var(--muted); margin-left: .3em; }
        .result .split { margin: .45rem 0 0; color: var(--muted); font-size: .95rem; }
        .gain { color: var(--gain); } .loss { color: var(--loss); } .flat { color: var(--muted); }

        .ribbon { margin: 1.75rem 0 2rem; overflow-x: auto; }
        .ribbon svg { width: 100%; min-width: 36rem; height: auto; display: block; }
        .ribbon .axis { stroke: var(--rule); stroke-width: 1; }
        .ribbon .hit { fill: transparent; }
        .ribbon a:hover .hit { fill: rgb(127 127 127 / .08); }
        .ribbon .bar.gain { fill: var(--gain); } .ribbon .bar.loss { fill: var(--loss); } .ribbon .bar.flat { fill: var(--muted); }
        .ribbon .bar.current { stroke: var(--signal); stroke-width: 3; paint-order: stroke; }
        .ribbon .refused { fill: none; stroke: var(--muted); stroke-width: 1.5; }
        .ribbon .refused.current { stroke: var(--signal); stroke-width: 3; }
        .ribbon .day { font: 14px var(--sans); fill: var(--muted); text-anchor: middle; }
        .ribbon .day.current { fill: var(--ink); font-weight: 700; }
        .ribbon figcaption { font-size: .85rem; color: var(--muted); margin-top: .25rem; }

        .facts { display: flex; flex-wrap: wrap; gap: .5rem 2.5rem; margin: 0 0 1.25rem; padding: 0; list-style: none; }
        .facts li { display: flex; flex-direction: column; }
        .facts .k { font-size: .85rem; color: var(--muted); }
        .facts .v { font-size: 1.15rem; font-weight: 600; }

        h2 { font: 500 1.35rem/1.2 var(--serif); margin: 2rem 0 .75rem; }
        .sheet { background: var(--sheet); border: 1px solid var(--rule); border-radius: 10px; overflow-x: auto; }
        table { width: 100%; border-collapse: collapse; }
        th, td { padding: .6rem .75rem; text-align: right; white-space: nowrap; }
        table.text th, table.text td { text-align: left; white-space: normal; }
        thead th { font-size: .85rem; font-weight: 600; color: var(--muted); border-bottom: 1px solid var(--rule); }
        tbody tr + tr td { border-top: 1px solid var(--rule); }
        th:first-child, td:first-child { text-align: left; }
        td.name { font-weight: 600; }
        td.name small { display: block; font-weight: 400; color: var(--muted); }
        tfoot td { border-top: 2px solid var(--ink); font-weight: 600; }

        .translated { margin: 1.5rem 0 0; padding: 1rem 1.25rem; border-left: 4px solid var(--binder); background: var(--sheet); border-radius: 0 10px 10px 0; }
        .translated p { margin: .2rem 0; }
        .translated .why { color: var(--muted); font-size: .9rem; }

        .stop { margin: 2rem 0; padding: 1.25rem 1.5rem; background: var(--sheet); border: 1px solid var(--rule);
                border-left: 6px solid var(--signal); border-radius: 0 10px 10px 0; }
        .stop h2 { margin-top: 0; }
        .stop ul { margin: .5rem 0 0; padding-left: 1.2rem; }

        .curve { margin: 1rem 0 0; }
        .curve svg { width: 100%; max-width: 34rem; height: auto; display: block; }
        .curve .grid { stroke: var(--rule); stroke-width: 1; }
        .curve .today { fill: none; stroke: var(--binder); stroke-width: 3; }
        .curve .before { fill: none; stroke: var(--muted); stroke-width: 1.5; stroke-dasharray: 5 4; }
        .curve .pt { fill: var(--binder); }
        .curve text { font: 12px var(--sans); fill: var(--muted); }
        .curve .val { fill: var(--ink); font-weight: 600; text-anchor: middle; }
        .curve figcaption { font-size: .85rem; color: var(--muted); }
        @media (prefers-color-scheme: dark) { .curve .today { stroke: var(--binder-ink); } .curve .pt { fill: var(--binder-ink); } }

        .status { font-weight: 600; }
        .status.open { color: var(--loss); }
        .status.resolved { color: var(--gain); }
        .note { color: var(--muted); font-size: .9rem; }

        footer { max-width: 76rem; margin: 0 auto; padding: 1.5rem; border-top: 1px solid var(--rule); color: var(--muted); font-size: .85rem; }
        footer p { margin: .3rem 0; max-width: 80ch; }

        @media (max-width: 44rem) {
          .day-head { grid-template-columns: 1fr; }
          .result { text-align: left; }
          .picker { margin-left: 0; }
        }
        """;
}
