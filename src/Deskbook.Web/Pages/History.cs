using System.Text;

namespace Deskbook.Web.Pages;

/// <summary>One published day's P&amp;L, or null when the book was refused that day.</summary>
internal sealed record PnlDay(DateOnly Date, decimal? Pnl);

/// <summary>
/// The P&amp;L history pane: one row per published day, newest first, each a link to that day's book, with a bar
/// in the cell drawn from the middle (losses to the left, gains to the right), the way a spreadsheet's data bars do.
/// Plain HTML and CSS: the bar is a sized span, so the figures stay readable as text.
/// </summary>
internal static class History
{
    public static string Pane(IReadOnlyList<PnlDay> days, DateOnly current, Func<DateOnly, string> link)
    {
        if (days.Count == 0) return "";
        var max = days.Where(d => d.Pnl is not null).Select(d => Math.Abs(d.Pnl!.Value)).DefaultIfEmpty(0m).Max();
        var html = new StringBuilder("<section class=\"pane history\"><h2>Day's P&amp;L, CAD</h2><table><tbody>");
        foreach (var day in days.OrderByDescending(d => d.Date))
        {
            var isCurrent = day.Date == current;
            html.Append(isCurrent ? "<tr class=\"current\">" : "<tr>")
                .Append($"<th scope=\"row\"><a href=\"{link(day.Date)}\"{(isCurrent ? " aria-current=\"date\"" : "")}>{Html.ShortDate(day.Date)}</a></th>");
            if (day.Pnl is { } pnl)
            {
                var pct = max == 0 ? 0 : Math.Round(Math.Abs(pnl) / max * 50, 1);
                var side = pnl < 0 ? "left" : "right";
                html.Append(Html.PnlCell(pnl))
                    .Append($"<td class=\"spark\"><span class=\"{side}{(pnl < 0 ? " loss" : "")}\" style=\"width:{pct.ToString(System.Globalization.CultureInfo.InvariantCulture)}%\"></span></td>");
            }
            else
            {
                html.Append("<td class=\"none\">no book</td><td class=\"spark\"></td>");
            }
            html.Append("</tr>");
        }
        return html.Append("</tbody></table></section>").ToString();
    }
}
