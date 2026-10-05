namespace LibraryApp.Domain.Borrowing;
using System;
using LibraryApp.Domain.Common;

/// <summary>
/// Represents penalty points associated with a staff member and a loan.
/// Controls waiver and expiry of the record.
/// </summary>
public sealed class PenaltyRecord : Entity
{
    /// <summary>
    /// Gets the identifier of the staff member receiving the penalty.
    /// </summary>
    public Guid StaffMemberId { get; private set; }

    /// <summary>
    /// Gets the identifier of the loan that caused the penalty.
    /// </summary>
    public Guid LoanId { get; private set; }

    /// <summary>
    /// Gets the original penalty points, between zero and ten.
    /// </summary>
    public int Points { get; private set; }

    /// <summary>
    /// Gets the explanation for applying the penalty.
    /// </summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which the points expire.
    /// </summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>
    /// Gets the current penalty state.
    /// </summary>
    public PenaltyRecordStatus Status { get; private set; }

    /// <summary>
    /// Gets the waiver explanation, or null if the record was not waived.
    /// </summary>
    public string? WaivedReason { get; private set; }

    /// <summary>
    /// Gets the database-managed optimistic concurrency token.
    /// </summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    // For EF Core when reconstructing an existing penalty.
    private PenaltyRecord()
    {
    }

    // Initializes the identifier through the shared Entity constructor.
    private PenaltyRecord(Guid id) : base(id)
    {
    }

    /// <summary>
    /// Creates an active penalty with a ninety-calendar-day validity period.
    /// </summary>
    /// <param name="id">
    /// The identifier obtained by Application from IIdGenerator.
    /// </param>
    /// <param name="staffMemberId">The affected staff member.</param>
    /// <param name="loanId">The loan that caused the penalty.</param>
    /// <param name="points">The calculated points, from zero to ten.</param>
    /// <param name="reason">A nonblank explanation of the penalty.</param>
    /// <param name="createdAtUtc">
    /// The UTC creation timestamp obtained from IClock.
    /// </param>
    /// <remarks>
    /// Application calculates the points and verifies that
    /// the supplied loan belongs to the supplied staff member.
    /// </remarks>
    public static PenaltyRecord Create(
        Guid id,
        Guid staffMemberId,
        Guid loanId,
        int points,
        string reason,
        DateTime createdAtUtc)
    {
        if (staffMemberId == Guid.Empty || loanId == Guid.Empty)
        {
            throw new DomainException(
                BorrowingErrors.InvalidReferenceId);
        }

        EnsureUtc(createdAtUtc);

        // Preserve the guide's agreed range.
        if (points < 0 || points > 10)
        {
            throw new DomainException(
                BorrowingErrors.InvalidPenaltyPoints);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                BorrowingErrors.PenaltyReasonRequired);
        }

        return new PenaltyRecord(id)
        {
            StaffMemberId = staffMemberId,
            LoanId = loanId,
            Points = points,
            Reason = reason.Trim(),
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = createdAtUtc.AddDays(90),
            Status = PenaltyRecordStatus.Active,
            WaivedReason = null
        };
    }

    /// <summary>
    /// Waives an active penalty and records the explanation.
    /// </summary>
    /// <param name="reason">The mandatory waiver explanation.</param>
    /// <remarks>
    /// Application enforces authorization before calling this method.
    /// The original points and penalty reason remain unchanged.
    /// </remarks>
    public void Waive(string reason)
    {
        if (Status != PenaltyRecordStatus.Active)
        {
            throw new DomainException(
                BorrowingErrors.PenaltyNotActive);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                BorrowingErrors.WaiverReasonRequired);
        }

        WaivedReason = reason.Trim();
        Status = PenaltyRecordStatus.Waived;
    }

    /// <summary>
    /// Marks an active penalty expired once its expiry timestamp is reached.
    /// </summary>
    /// <param name="nowUtc">The current UTC timestamp.</param>
    public void Expire(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);

        if (Status != PenaltyRecordStatus.Active)
        {
            throw new DomainException(
                BorrowingErrors.PenaltyNotActive);
        }

        // Preserve the guide's boundary: equality is expired.
        if (nowUtc < ExpiresAtUtc)
        {
            throw new DomainException(
                BorrowingErrors.PenaltyNotDueToExpire);
        }

        Status = PenaltyRecordStatus.Expired;
    }

    /// <summary>
    /// Determines whether this record currently contributes points
    /// at the supplied timestamp.
    /// </summary>
    /// <param name="nowUtc">The UTC timestamp to evaluate.</param>
    /// <returns>
    /// True when the current status is active and the timestamp
    /// is within the record's validity period.
    /// </returns>
    /// <remarks>
    /// Evaluates the record's current state.
    /// Does not reconstruct historical states before a waiver or expiry.
    /// </remarks>
    public bool CountsAt(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);

        return Status == PenaltyRecordStatus.Active
            && nowUtc >= CreatedAtUtc
            && nowUtc < ExpiresAtUtc;
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