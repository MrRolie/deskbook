using Deskbook.Core;

namespace Deskbook.Web.Pages;

internal enum Tab { Fx, Bonds, Breaks }

/// <summary>
/// The shell every view shares: the desk's bar, the three sheets as tabs, and one footer line with the fictional
/// mark and the data's attribution. One stylesheet, inline: the page loads nothing from anywhere else.
/// </summary>
internal static class Layout
{
    /// <param name="currency">The second currency in use, carried to the other sheets. The date is not carried:
    /// each book has its own latest day (yields are published on their own schedule), so a tab opens its own.</param>
    public static string Page(string title, Tab active, string? currency, string body, string source)
    {
        var carried = currency is null ? "" : "?ccy=" + Html.E(currency);
        string TabLink(Tab tab, string href, string label) =>
            $"<a href=\"{href}{carried}\"{(tab == active ? " aria-current=\"page\"" : "")}>{label}</a>";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <title>{{Html.E(title)}} | deskbook</title>
            <style>{{Css}}</style>
            </head>
            <body>
            <header class="bar"><span class="brand">deskbook</span><span class="desk">{{Html.E(Attribution.Desk)}}</span></header>
            <nav class="tabs" aria-label="Sheets">{{TabLink(Tab.Fx, "/", "FX book")}}{{TabLink(Tab.Bonds, "/bonds", "Bond book")}}{{TabLink(Tab.Breaks, "/breaks", "Breaks")}}</nav>
            <main class="sheet">
            {{body}}
            </main>
            <footer>{{Html.E(Attribution.Fictional)}} {{Html.E(source)}}</footer>
            </body>
            </html>
            """;
    }

    private const string Css = """
        :root {
          --ink: #14213d; --muted: #586674; --chrome: #e3e9e7; --sheet: #fcfdfc; --head: #d3dedb; --rule: #a7b6b2;
          --hair: #d9e1df; --binder: #1f4e5f; --binder-ink: #eef4f2; --loss: #b3402a; --gain: #1e7a4f; --signal: #d9a21b;
          --bar: #7f9c96;
          font-family: "IBM Plex Sans", "Segoe UI", "Ubuntu Sans", Roboto, "Helvetica Neue", Arial, sans-serif;
        }
        @media (prefers-color-scheme: dark) {
          :root { --ink: #e2eae8; --muted: #9aaba7; --chrome: #0e171b; --sheet: #141f24; --head: #1e2e35; --rule: #3a4f56;
                  --hair: #24363d; --binder: #173a47; --binder-ink: #dbe8e5; --loss: #f0866f; --gain: #5cc490; --bar: #5f7f79; }
        }
        * { box-sizing: border-box; border-radius: 0; }
        html { background: var(--chrome); color: var(--ink); }
        body { margin: 0; font-size: 14px; line-height: 1.4; font-variant-numeric: tabular-nums lining-nums; }
        a { color: inherit; }
        :focus-visible { outline: 2px solid var(--signal); outline-offset: 1px; }

        .bar { background: var(--binder); color: var(--binder-ink); padding: .45rem 1rem; display: flex; gap: 1rem; align-items: baseline; }
        .bar .brand { font-weight: 700; font-size: 1.05rem; letter-spacing: .02em; }
        .bar .desk { opacity: .85; }

        .tabs { display: flex; gap: 2px; padding: .6rem 1rem 0; border-bottom: 1px solid var(--rule); }
        .tabs a { padding: .35rem .9rem; text-decoration: none; background: var(--head); border: 1px solid var(--rule); border-bottom: 0; color: var(--muted); }
        .tabs a:hover { color: var(--ink); }
        .tabs a[aria-current="page"] { background: var(--sheet); color: var(--ink); font-weight: 600; margin-bottom: -1px; padding-bottom: calc(.35rem + 1px); }
        .sheet { background: var(--sheet); padding: .9rem 1rem 1.2rem; }

        .toolbar { display: flex; flex-wrap: wrap; gap: .5rem 1.25rem; align-items: center; padding-bottom: .75rem; margin-bottom: .9rem; border-bottom: 1px solid var(--hair); }
        .toolbar h1 { font-size: 1.15rem; margin: 0; font-weight: 600; }
        .toolbar .step { color: var(--muted); }
        .toolbar form { display: flex; gap: .4rem; align-items: center; margin-left: auto; }
        .toolbar input, .toolbar select, .toolbar button { font: inherit; color: var(--ink); background: var(--sheet); border: 1px solid var(--rule); padding: .2rem .4rem; }
        .toolbar button { background: var(--head); cursor: pointer; }
        .toolbar .json { color: var(--muted); font-size: .9em; }

        .grid { display: grid; grid-template-columns: minmax(0, 1fr) 19rem; gap: 1rem; align-items: start; }
        .stack { display: grid; gap: 1rem; min-width: 0; }
        .pane { border: 1px solid var(--rule); background: var(--sheet); min-width: 0; }
        .pane > h2 { margin: 0; padding: .3rem .6rem; font-size: .95rem; font-weight: 600; background: var(--head); border-bottom: 1px solid var(--rule); display: flex; gap: .75rem; align-items: baseline; }
        .pane > h2 small { font-weight: 400; color: var(--muted); font-size: .9em; }
        .pane .scroll { overflow-x: auto; }
        .pane > p { margin: .5rem .6rem; }

        table { width: 100%; border-collapse: collapse; }
        table.narrow { width: auto; min-width: min(100%, 24rem); }
        th, td { padding: .28rem .6rem; text-align: right; white-space: nowrap; }
        thead th { font-weight: 600; color: var(--muted); border-bottom: 1px solid var(--rule); vertical-align: bottom; }
        tbody tr + tr > * { border-top: 1px solid var(--hair); }
        th:first-child, td:first-child { text-align: left; }
        tbody th { font-weight: 600; }
        th small, td small { display: block; font-weight: 400; color: var(--muted); }
        tfoot td, tfoot th { font-weight: 600; }
        tfoot tr:first-child > * { border-top: 1px solid var(--ink); }
        tfoot tr.result > * { border-top: 1px solid var(--rule); }
        tfoot tr.result td.sum { border-bottom: 3px double var(--ink); }
        tfoot .label { font-weight: 400; color: var(--muted); }
        .loss { color: var(--loss); }

        .history td.none { color: var(--muted); font-style: italic; }
        .history td.spark { width: 40%; position: relative; padding: 0; }
        .history td.spark::before { content: ""; position: absolute; left: 50%; top: 0; bottom: 0; border-left: 1px solid var(--rule); }
        .history td.spark span { position: absolute; top: .4rem; bottom: .4rem; background: var(--bar); }
        .history td.spark span.right { left: 50%; }
        .history td.spark span.left { right: 50%; }
        .history td.spark span.loss { background: var(--loss); }
        .history tr.current > * { background: color-mix(in srgb, var(--signal) 18%, var(--sheet)); }
        .history tr.current th { box-shadow: inset 3px 0 0 var(--signal); }
        .history th a { text-decoration: none; font-weight: 400; }
        .history tr.current th a { font-weight: 700; }
        .history th a:hover { text-decoration: underline; }

        .stop > h2 { background: color-mix(in srgb, var(--signal) 35%, var(--sheet)); }
        .stop ul { margin: .5rem .6rem .7rem; padding-left: 1.2rem; }

        .curve svg { display: block; width: 100%; height: auto; }
        .curve .grid { stroke: var(--hair); }
        .curve .axis { stroke: var(--rule); }
        .curve .today { fill: none; stroke: var(--binder); stroke-width: 2; }
        .curve .before { fill: none; stroke: var(--muted); stroke-width: 1; stroke-dasharray: 4 3; }
        .curve .pt { fill: var(--binder); }
        .curve text { font-size: 11px; fill: var(--muted); }
        .curve .val { fill: var(--ink); font-weight: 600; text-anchor: middle; }
        @media (prefers-color-scheme: dark) { .curve .today { stroke: var(--binder-ink); } .curve .pt { fill: var(--binder-ink); } }

        table.text td, table.text th { white-space: normal; text-align: left; vertical-align: top; }
        .status { display: inline-block; padding: 0 .35rem; border: 1px solid currentColor; font-weight: 600; }
        .status.open { color: var(--loss); }
        .status.resolved { color: var(--gain); }

        footer { padding: .6rem 1rem 1.5rem; color: var(--muted); font-size: .82rem; max-width: 110ch; }

        @media (max-width: 60rem) {
          .grid { grid-template-columns: minmax(0, 1fr); }
          .toolbar form { margin-left: 0; }
        }
        """;
}
