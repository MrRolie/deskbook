namespace Deskbook.Core.Reconciliation;

/// <summary>Why a trade and a confirmation do not agree.</summary>
public enum BreakKind
{
    /// <summary>The desk booked a trade that no confirmation quotes.</summary>
    MissingConfirmation,
    /// <summary>A confirmation quotes a trade id the desk never booked.</summary>
    UnknownTrade,
    /// <summary>A second confirmation quotes a trade that is already confirmed.</summary>
    DuplicateConfirmation,
    /// <summary>The trade and its confirmation differ on one field.</summary>
    FieldMismatch,
}

/// <summary>
/// One break. <see cref="Field"/>, <see cref="DeskValue"/> and <see cref="CounterpartyValue"/> are set for a field
/// mismatch; <see cref="Detail"/> says in words what is wrong and, where it can, what might explain it.
/// </summary>
public sealed record Break(
    BreakKind Kind,
    string? TradeId,
    string? ConfirmationId,
    string? Field,
    string? DeskValue,
    string? CounterpartyValue,
    string Detail);

/// <summary>The outcome of one reconciliation: how many trades agreed in full, and every break, in a stable order.</summary>
public sealed record ReconciliationResult(int TradesCompared, int ConfirmationsCompared, int Matched, IReadOnlyList<Break> Breaks);
