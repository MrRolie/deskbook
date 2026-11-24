namespace Deskbook.Core.Reconciliation;

/// <summary>
/// Reads counterparty confirmations from a CSV file with this exact header:
/// <c>confirmation_id,trade_id,trade_date,counterparty,desk_bought_currency,desk_bought_amount,desk_sold_currency,desk_sold_amount</c>.
/// Amounts are stated from the desk's side. Unlike a trade, a confirmation is read as received: its amounts may be
/// wrong, and that is what reconciliation is for, so only the format is checked here.
/// </summary>
public static class ConfirmationCsv
{
    public const string Header =
        "confirmation_id,trade_id,trade_date,counterparty,desk_bought_currency,desk_bought_amount,desk_sold_currency,desk_sold_amount";

    public static IReadOnlyList<Confirmation> Parse(TextReader reader) =>
        StrictCsv.Read(reader, Header, f =>
        {
            if (string.IsNullOrWhiteSpace(f[0])) throw new FormatException("a confirmation needs an id");
            return new Confirmation(
                ConfirmationId: f[0].Trim(),
                TradeId: f[1].Trim(),
                TradeDate: StrictCsv.Date(f[2]),
                Counterparty: f[3].Trim(),
                Bought: new Leg(new Currency(f[4].Trim()), StrictCsv.Amount(f[5])),
                Sold: new Leg(new Currency(f[6].Trim()), StrictCsv.Amount(f[7])));
        }, c => c.ConfirmationId);
}
