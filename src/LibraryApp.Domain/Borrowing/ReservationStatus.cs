namespace LibraryApp.Domain.Borrowing;

/// <summary>
/// Represents the lifecycle state of a reservation for a book.
/// </summary>
public enum ReservationStatus
{
    /// <summary>
    /// The staff member is waiting in the book's reservation queue.
    /// </summary>
    Waiting = 1,

    /// <summary>
    /// A copy is being held for the staff member to collect.
    /// </summary>
    ReadyForPickup = 2,

    /// <summary>
    /// The staff member collected a copy and a loan was created.
    /// </summary>
    Fulfilled = 3,

    /// <summary>
    /// The reservation was cancelled.
    /// </summary>
    Cancelled = 4,

    /// <summary>
    /// The pickup deadline passed without collection.
    /// </summary>
    Expired = 5
}