using System.Globalization;
using System.Text;

namespace Deskbook.Web.Pages;

/// <summary>One published day on the ribbon: its P&amp;L, or null when the book was refused that day.</summary>
internal sealed record RibbonDay(DateOnly Date, decimal? Pnl);

/// <summary>
/// The P&amp;L ribbon: one bar per published day, gains above the line and losses below, each bar a link to that
/// day's book. Inline SVG, no script. The current day is outlined; a refused day is a hollow mark on the line.
/// </summary>
internal static class Ribbon
{
    private const int Width = 960, Height = 132, Mid = 60, Reach = 50;

    public static string Render(IReadOnlyList<RibbonDay> days, DateOnly current, Func<DateOnly, string> link, string currency)
    {
        if (days.Count == 0) return "";
        var max = days.Where(d => d.Pnl is not null).Select(d => Math.Abs(d.Pnl!.Value)).DefaultIfEmpty(0m).Max();
        var width = Width;
        var slot = (double)Width / days.Count;
        var bar = Math.Min(44.0, slot * 0.62);
        var svg = new StringBuilder();
        svg.Append($"<figure class=\"ribbon\"><svg viewBox=\"0 0 {width} {Height}\" role=\"img\" aria-labelledby=\"ribbon-title\">");
        svg.Append($"<title id=\"ribbon-title\">Day's P&amp;L in {currency} for each published day; select a bar to open that day</title>");
        svg.Append($"<line class=\"axis\" x1=\"0\" x2=\"{width}\" y1=\"{Mid}\" y2=\"{Mid}\"/>");

        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            var x = i * slot + (slot - bar) / 2;
            var centre = i * slot + slot / 2;
            var label = day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var isCurrent = day.Date == current;
            svg.Append($"<a href=\"{link(day.Date)}\" aria-label=\"{Html.LongDate(day.Date)}: " +
                       $"{(day.Pnl is null ? "no book" : Html.Signed(day.Pnl.Value) + " " + currency)}\"{(isCurrent ? " aria-current=\"date\"" : "")}>");
            svg.Append($"<rect class=\"hit\" x=\"{i * slot:0.#}\" y=\"0\" width=\"{slot:0.#}\" height=\"{Height}\"/>");
            if (day.Pnl is { } pnl && max > 0)
            {
                var h = Math.Max(2, (int)Math.Round((double)(Math.Abs(pnl) / max) * Reach));
                var y = pnl >= 0 ? Mid - h : Mid;
                svg.Append($"<rect class=\"bar {Html.Tone(pnl)}{(isCurrent ? " current" : "")}\" x=\"{x:0.#}\" y=\"{y}\" width=\"{bar:0.#}\" height=\"{h}\" rx=\"2\"/>");
            }
            else
            {
                svg.Append($"<circle class=\"refused{(isCurrent ? " current" : "")}\" cx=\"{centre:0.#}\" cy=\"{Mid}\" r=\"6\"/>");
            }
            svg.Append($"<text class=\"{(isCurrent ? "day current" : "day")}\" x=\"{centre:0.#}\" y=\"{Height - 6}\">{day.Date.Day}</text>");
            svg.Append("</a>");
        }
        svg.Append("</svg>");
        svg.Append($"<figcaption>Day's P&amp;L, {Html.E(currency)}, from {Html.LongDate(days[0].Date)} to {Html.LongDate(days[^1].Date)}. " +
                   "A hollow mark is a day with no book.</figcaption></figure>");
        return svg.ToString();
    }
}
