using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Borrowing;

/// <summary>
/// Represents the borrowing of one physical book copy by a staff member.
/// Controls the loan's lifecycle and protects its internal state.
/// </summary>
public sealed class Loan : Entity
{
    public const int ReturnNoteMaxLength = 500;
    /// <summary>
    /// Gets the identifier of the borrowed physical copy.
    /// </summary>
    public Guid BookCopyId { get; private set; }

    /// <summary>
    /// Gets the identifier of the staff member who borrowed the copy.
    /// </summary>
    public Guid StaffMemberId { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the loan was issued.
    /// </summary>
    public DateTime IssuedAtUtc { get; private set; }

    /// <summary>
    /// Gets the current UTC deadline for returning the copy.
    /// </summary>
    public DateTime DueAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC return timestamp, or null while the loan is open.
    /// </summary>
    /// <remarks>
    /// Provisionally also stores the closing timestamp for a lost loan,
    /// following the current project guide.
    /// </remarks>
    public DateTime? ReturnedAtUtc { get; private set; }

    /// <summary>
    /// Gets the optional note recorded when the copy was returned.
    /// </summary>
    public string? ReturnNote { get; private set; }
    
    /// <summary>
    /// Gets the number of successful renewals.
    /// </summary>
    public int RenewalCount { get; private set; }

    /// <summary>
    /// Gets the current lifecycle state.
    /// </summary>
    public LoanStatus Status { get; private set; }

    /// <summary>
    /// Gets the database-managed optimistic concurrency token.
    /// </summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets whether the loan remains open.
    /// Active and overdue loans are both open.
    /// </summary>
    public bool IsOpen =>
        Status == LoanStatus.Active ||
        Status == LoanStatus.Overdue;

    // For EF Core when reconstructing an existing loan.
    private Loan()
    {
    }

    // Passes the identifier to the shared Entity constructor.
    private Loan(Guid id) : base(id)
    {
    }

    /// <summary>
    /// Creates an active loan for a physical copy and staff member.
    /// </summary>
    /// <param name="id">
    /// The loan identifier obtained by Application from IIdGenerator.
    /// </param>
    /// <param name="bookCopyId">The physical copy being borrowed.</param>
    /// <param name="staffMemberId">The borrowing staff member.</param>
    /// <param name="issuedAtUtc">
    /// The UTC issue timestamp obtained by Application from IClock.
    /// </param>
    /// <param name="loanDays">
    /// The positive loan duration in calendar days.
    /// </param>
    /// <returns>A new active loan.</returns>
    /// <remarks>
    /// Application checks member eligibility, loan limits,
    /// and copy availability before calling this method.
    /// </remarks>
    public static Loan Issue(
        Guid id,
        Guid bookCopyId,
        Guid staffMemberId,
        DateTime issuedAtUtc,
        int loanDays)
    {
        if (bookCopyId == Guid.Empty || staffMemberId == Guid.Empty)
        {
            throw new DomainException(
                BorrowingErrors.InvalidReferenceId);
        }

        EnsureUtc(issuedAtUtc);

        if (loanDays <= 0)
        {
            throw new DomainException(
                BorrowingErrors.InvalidLoanDays);
        }

        return new Loan(id)
        {
            BookCopyId = bookCopyId,
            StaffMemberId = staffMemberId,
            IssuedAtUtc = issuedAtUtc,

            // Preserve the guide's calendar-day calculation.
            DueAtUtc = issuedAtUtc.AddDays(loanDays),

            ReturnedAtUtc = null,
            RenewalCount = 0,
            Status = LoanStatus.Active
        };
    }

    /// <summary>
    /// Extends an active loan from its existing deadline.
    /// </summary>
    /// <param name="loanDays">
    /// The positive extension in calendar days.
    /// </param>
    /// <remarks>
    /// Application checks the actual deadline, maximum renewals,
    /// member status, and reservation queue before calling this method.
    /// </remarks>
    public void Renew(int loanDays)
    {
        if (Status != LoanStatus.Active)
        {
            throw new DomainException(
                BorrowingErrors.LoanCannotBeRenewed);
        }

        if (loanDays <= 0)
        {
            throw new DomainException(
                BorrowingErrors.InvalidLoanDays);
        }

        // Calculate both values before changing the entity.
        DateTime newDueAtUtc = DueAtUtc.AddDays(loanDays);
        int newRenewalCount = checked(RenewalCount + 1);

        DueAtUtc = newDueAtUtc;
        RenewalCount = newRenewalCount;
    }

    /// <summary>
    /// Marks an active loan overdue once its deadline has passed.
    /// </summary>
    /// <param name="nowUtc">The current UTC timestamp.</param>
    public void MarkOverdue(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);

        if (Status != LoanStatus.Active)
        {
            throw new DomainException(
                BorrowingErrors.LoanCannotBecomeOverdue);
        }

        // Current convention: exactly at the deadline is not overdue.
        if (nowUtc <= DueAtUtc)
        {
            throw new DomainException(
                BorrowingErrors.LoanNotPastDue);
        }

        Status = LoanStatus.Overdue;
    }
    /// <summary>
    /// Closes an active or overdue loan and records an optional return note.
    /// </summary>
    /// <param name="returnedAtUtc">The UTC return timestamp.</param>
    /// <param name="note">An optional note, at most 500 characters after trimming.</param>
    /// <remarks>
    /// Application coordinates penalties, copy availability,
    /// and reservation processing.
    /// </remarks>
    public void Return(DateTime returnedAtUtc, string? note = null)
    {
        EnsureUtc(returnedAtUtc);

        if (!IsOpen)
        {
            throw new DomainException(
                BorrowingErrors.LoanAlreadyClosed);
        }

        if (returnedAtUtc < IssuedAtUtc)
        {
            throw new DomainException(
                BorrowingErrors.LoanClosingBeforeIssue);
        }

        string? normalizedNote =
            string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        // Also checked by the request validator; repeated here for callers without it (pages, jobs).
        if (normalizedNote is { Length: > ReturnNoteMaxLength })
        {
            throw new DomainException(BorrowingErrors.ReturnNoteTooLong);
        }

        // Change state only after every check succeeds.
        ReturnedAtUtc = returnedAtUtc;
        ReturnNote = normalizedNote;
        Status = LoanStatus.Returned;
    }

    /// <summary>
    /// Closes an open loan when the copy is declared lost.
    /// </summary>
    /// <param name="declaredAtUtc">
    /// The UTC timestamp when the copy was declared lost.
    /// </param>
    /// <remarks>
    /// PROVISIONAL: Active to Lost is permitted pending BQ-BA-06.
    /// PROVISIONAL: ReturnedAtUtc stores the lost-loan closing timestamp.
    /// Application coordinates the Catalog update and penalty.
    /// </remarks>
    public void MarkLost(DateTime declaredAtUtc)
    {
        EnsureUtc(declaredAtUtc);

        if (!IsOpen)
        {
            throw new DomainException(
                BorrowingErrors.LoanAlreadyClosed);
        }

        if (declaredAtUtc < IssuedAtUtc)
        {
            throw new DomainException(
                BorrowingErrors.LoanClosingBeforeIssue);
        }

        // Provisional convention retained from the project guide.
        ReturnedAtUtc = declaredAtUtc;
        Status = LoanStatus.Lost;
    }

    /// <summary>
    /// Rejects timestamps that are not explicitly UTC.
    /// </summary> 
    private static void EnsureUtc(DateTime timestamp)
    {
        if (timestamp.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                BorrowingErrors.TimestampMustBeUtc);
        }
    }
}   