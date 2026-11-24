namespace Deskbook.Core;

/// <summary>
/// Thrown when a book cannot be produced honestly from the stored data, for example a missing rate.
/// The book is refused rather than guessed; <see cref="Reasons"/> says what is missing.
/// </summary>
public sealed class BookRefusedException : Exception
{
    public IReadOnlyList<string> Reasons { get; }

    public BookRefusedException(IReadOnlyList<string> reasons)
        : base("Book refused: " + string.Join("; ", reasons))
    {
        Reasons = reasons;
    }

    public BookRefusedException(string reason) : this(new[] { reason }) { }
}
