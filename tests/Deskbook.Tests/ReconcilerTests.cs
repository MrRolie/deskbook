using Deskbook.Core;
using Deskbook.Core.Reconciliation;
using static Deskbook.Tests.Build;

namespace Deskbook.Tests;

public class ReconcilerTests
{
    private static Confirmation Confirm(string id, Trade t, decimal? soldAmount = null, string? tradeId = null, string? date = null) =>
        new(id, tradeId ?? t.Id, date is null ? t.TradeDate : D(date), t.Counterparty, t.Bought, t.Sold with { Amount = soldAmount ?? t.Sold.Amount });

    private static readonly Trade T1 = Trade("FICT-1", "2026-11-03", "USD", 1_000_000m, "CAD", 1_399_000m);
    private static readonly Trade T2 = Trade("FICT-2", "2026-11-05", "CAD", 784_600m, "EUR", 500_000m);

    [Fact]
    public void Trades_that_agree_with_their_confirmations_give_no_breaks()
    {
        var result = Reconciler.Reconcile(new[] { T1, T2 }, new[] { Confirm("C1", T1), Confirm("C2", T2) });
        Assert.Equal(2, result.Matched);
        Assert.Empty(result.Breaks);
    }

    [Fact]
    public void Amounts_compare_as_numbers_not_as_text()
    {
        var result = Reconciler.Reconcile(new[] { T1 }, new[] { Confirm("C1", T1, soldAmount: 1_399_000.00m) });
        Assert.Empty(result.Breaks);
    }

    [Fact]
    public void A_different_amount_is_one_field_break_with_both_values()
    {
        var result = Reconciler.Reconcile(new[] { T1 }, new[] { Confirm("C1", T1, soldAmount: 1_399_500m) });

        var b = Assert.Single(result.Breaks);
        Assert.Equal((BreakKind.FieldMismatch, "sold_amount", "1399000", "1399500"), (b.Kind, b.Field, b.DeskValue, b.CounterpartyValue));
        Assert.Equal(0, result.Matched);
    }

    [Fact]
    public void A_second_confirmation_of_the_same_trade_is_a_duplicate()
    {
        var result = Reconciler.Reconcile(new[] { T1 }, new[] { Confirm("C1", T1), Confirm("C2", T1) });
        var b = Assert.Single(result.Breaks);
        Assert.Equal((BreakKind.DuplicateConfirmation, "C2"), (b.Kind, b.ConfirmationId));
    }

    [Fact]
    public void A_wrong_reference_is_an_unknown_trade_that_names_the_trade_it_looks_like()
    {
        var result = Reconciler.Reconcile(new[] { T1, T2 }, new[] { Confirm("C1", T1), Confirm("C2", T2, tradeId: "FICT-22") });

        Assert.Equal(2, result.Breaks.Count);
        Assert.Contains(result.Breaks, b => b.Kind == BreakKind.MissingConfirmation && b.TradeId == "FICT-2");
        var unknown = Assert.Single(result.Breaks, b => b.Kind == BreakKind.UnknownTrade);
        Assert.Contains("same economics as unconfirmed trade(s) FICT-2", unknown.Detail);
    }

    [Fact]
    public void The_sample_files_give_exactly_the_four_planted_breaks()
    {
        using var tradeFile = new StreamReader(Sample("trades-fictional.csv"));
        using var confirmationFile = new StreamReader(Sample("confirmations-fictional.csv"));
        var result = Reconciler.Reconcile(TradeCsv.Parse(tradeFile), ConfirmationCsv.Parse(confirmationFile));

        Assert.Equal(5, result.Matched);
        Assert.Equal(new[]
        {
            (BreakKind.FieldMismatch, "FICT-0003", "sold_amount"),
            (BreakKind.MissingConfirmation, "FICT-0006", (string?)null),
            (BreakKind.FieldMismatch, "FICT-0007", "trade_date"),
            (BreakKind.UnknownTrade, (string?)null, (string?)null),
        }, result.Breaks.Select(b => (b.Kind, b.TradeId, b.Field)));
    }

    [Fact]
    public void The_same_inputs_give_the_same_breaks_in_the_same_order()
    {
        var confirmations = new[] { Confirm("C9", T2, tradeId: "X"), Confirm("C1", T1, soldAmount: 1m) };
        var first = Reconciler.Reconcile(new[] { T1, T2 }, confirmations);
        var second = Reconciler.Reconcile(new[] { T2, T1 }, confirmations.Reverse().ToArray());
        Assert.Equal(first.Breaks, second.Breaks);
    }
}
