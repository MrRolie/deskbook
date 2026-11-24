namespace Deskbook.Core;

/// <summary>Fixed text the book carries with every output: the source of its rates and that its data is invented.</summary>
public static class Attribution
{
    public const string Desk = "Demo FX Desk (fictional)";

    public const string Fictional =
        "The desk, its counterparties, trades and positions are invented. Only the exchange rates are real.";

    public const string Source =
        "Exchange rates: Bank of Canada, Valet API (https://www.bankofcanada.ca/valet). Indicative rates. " +
        "Values reproduced unchanged; series and dates selected. The Bank of Canada does not endorse this project.";
}
