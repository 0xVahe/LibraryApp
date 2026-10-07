    using LibraryApp.Domain.Borrowing;

    namespace LibraryApp.Application.Borrowing.Dto;

    /// <summary>
    /// What the API returns about a loan. The Loan entity is never returned directly.
    /// </summary>
    /// <param name="LoanId">The identifier of the loan.</param>
    /// <param name="BookCopyId">The physical copy that was lent.</param>
    /// <param name="StaffMemberId">The employee who holds the copy.</param>
    /// <param name="IssuedAtUtc">When the loan started (UTC).</param>
    /// <param name="DueAtUtc">When the copy must be returned (UTC).</param>
    /// <param name="RenewalCount">How many times the loan was renewed so far.</param>
    /// <param name="Status">The current loan status.</param>
    public sealed record LoanDto(
        Guid LoanId,
        Guid BookCopyId,
        Guid StaffMemberId,
        DateTime IssuedAtUtc,
        DateTime DueAtUtc,
        int RenewalCount,
        LoanStatus Status);