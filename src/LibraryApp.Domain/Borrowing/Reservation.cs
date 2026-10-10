using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Borrowing;

/// <summary>
/// Represents a staff member's reservation for a book.
/// Controls queue membership and the reservation lifecycle.
/// </summary>
public sealed class Reservation : Entity
{
    /// <summary>
    /// Gets the identifier of the copy assigned for collection.
    /// Null until the reservation becomes ready.
    /// Retained after completion for historical reference.
    /// </summary>
    public Guid? CopyId { get; private set; }
    
    /// <summary>
    /// Gets the identifier of the reserved book.
    /// </summary>
    public Guid BookId { get; private set; }

    /// <summary>
    /// Gets the identifier of the staff member who made the reservation.
    /// </summary>
    public Guid StaffMemberId { get; private set; }

    /// <summary>
    /// Gets the UTC creation timestamp used for queue ordering.
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Gets the current reservation state.
    /// </summary>
    public ReservationStatus Status { get; private set; }

    /// <summary>
    /// Gets the UTC pickup deadline.
    /// Null until the reservation becomes ready for collection.
    /// </summary>
    public DateTime? ReadyUntilUtc { get; private set; }

    /// <summary>
    /// Gets the positive queue position while waiting,
    /// or zero in every other state.
    /// </summary>
    public int QueuePosition { get; private set; }

    /// <summary>
    /// Gets the database-managed optimistic concurrency token.
    /// </summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets whether the reservation is waiting or ready for collection.
    /// </summary>
    public bool IsActive =>
        Status == ReservationStatus.Waiting ||
        Status == ReservationStatus.ReadyForPickup;

    // For EF Core when reconstructing an existing reservation.
    private Reservation()
    {
    }

    // Initializes the identifier through the shared Entity constructor.
    private Reservation(Guid id) : base(id)
    {
    }

    /// <summary>
    /// Creates a waiting reservation at the supplied queue position.
    /// </summary>
    /// <param name="id">
    /// The identifier obtained by Application from IIdGenerator.
    /// </param>
    /// <param name="bookId">The book being reserved.</param>
    /// <param name="staffMemberId">The staff member making the reservation.</param>
    /// <param name="createdAtUtc">
    /// The UTC creation timestamp obtained from IClock.
    /// </param>
    /// <param name="queuePosition">
    /// The positive queue position determined by Application.
    /// </param>
    /// <remarks>
    /// Application checks eligibility, reservation limits,
    /// duplicate reservations, and the correct queue position.
    /// </remarks>
    public static Reservation Create(
        Guid id,
        Guid bookId,
        Guid staffMemberId,
        DateTime createdAtUtc,
        int queuePosition)
    {
        if (bookId == Guid.Empty || staffMemberId == Guid.Empty)
        {
            throw new DomainException(
                BorrowingErrors.InvalidReferenceId);
        }

        EnsureUtc(createdAtUtc);

        if (queuePosition <= 0)
        {
            throw new DomainException(
                BorrowingErrors.InvalidQueuePosition);
        }

        return new Reservation(id)
        {
            BookId = bookId,
            StaffMemberId = staffMemberId,
            CreatedAtUtc = createdAtUtc,
            Status = ReservationStatus.Waiting,
            ReadyUntilUtc = null,
            QueuePosition = queuePosition
        };
    }

    /// <summary>
    /// Assigns a copy and makes a waiting reservation ready for collection.
    /// </summary>
    /// <param name="copyId">The copy assigned to this reservation.</param>
    /// <param name="readyUntilUtc">
    /// The UTC pickup deadline calculated by Application.
    /// </param>
    /// <remarks>
    /// Application verifies that the copy belongs to the reserved book,
    /// checks queue priority, arranges the copy hold,
    /// and calculates the deadline using the working-day calendar.
    /// The copy hold and reservation update must commit together.
    /// </remarks>
    public void MarkReady(Guid copyId, DateTime readyUntilUtc)
    {
        EnsureUtc(readyUntilUtc);

        if (Status != ReservationStatus.Waiting)
        {
            throw new DomainException(
                BorrowingErrors.ReservationNotWaiting);
        }

        if (copyId == Guid.Empty)
        {
            throw new DomainException(
                BorrowingErrors.InvalidReferenceId);
        }

        if (readyUntilUtc <= CreatedAtUtc)
        {
            throw new DomainException(
                BorrowingErrors.InvalidPickupDeadline);
        }

        CopyId = copyId;
        ReadyUntilUtc = readyUntilUtc;

        // The member leaves the waiting queue.
        QueuePosition = 0;
        Status = ReservationStatus.ReadyForPickup;
    }

    /// <summary>
    /// Completes a ready reservation when the staff member collects a copy.
    /// </summary>
    /// <remarks>
    /// Application verifies the pickup deadline and borrowing eligibility,
    /// then creates the loan in the same transaction.
    /// </remarks>
    public void Fulfil()
    {
        if (Status != ReservationStatus.ReadyForPickup)
        {
            throw new DomainException(
                BorrowingErrors.ReservationNotReady);
        }

        QueuePosition = 0;
        Status = ReservationStatus.Fulfilled;
    }

    /// <summary>
    /// Cancels a waiting or ready reservation.
    /// </summary>
    /// <remarks>
    /// Application releases any held copy and updates the remaining queue.
    /// </remarks>
    public void Cancel()
    {
        if (!IsActive)
        {
            throw new DomainException(
                BorrowingErrors.ReservationCannotBeCancelled);
        }

        QueuePosition = 0;
        Status = ReservationStatus.Cancelled;
    }

    /// <summary>
    /// Expires a ready reservation after its pickup deadline has passed.
    /// </summary>
    /// <param name="nowUtc">The current UTC timestamp.</param>
    public void Expire(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);

        if (Status != ReservationStatus.ReadyForPickup)
        {
            throw new DomainException(
                BorrowingErrors.ReservationNotReady);
        }

        // Preserve the guide's boundary: equality is not yet expired.
        if (ReadyUntilUtc is null || nowUtc <= ReadyUntilUtc.Value)
        {
            throw new DomainException(
                BorrowingErrors.PickupDeadlineNotPassed);
        }

        QueuePosition = 0;
        Status = ReservationStatus.Expired;
    }

    /// <summary>
    /// Changes the position of a waiting reservation.
    /// </summary>
    /// <param name="position">The new positive queue position.</param>
    /// <remarks>
    /// Application renumbers the complete waiting queue
    /// within the same transaction.
    /// </remarks>
    public void SetQueuePosition(int position)
    {
        if (Status != ReservationStatus.Waiting)
        {
            throw new DomainException(
                BorrowingErrors.ReservationNotWaiting);
        }

        if (position <= 0)
        {
            throw new DomainException(
                BorrowingErrors.InvalidQueuePosition);
        }

        QueuePosition = position;
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