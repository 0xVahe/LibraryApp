namespace LibraryApp.Application.Abstractions.Borrowing;

/// <summary>
/// Database lock resources (IApplicationLock) of Borrowing, shared with the modules that must not run at the same time
/// as a Borrowing operation. Take them in this order only — staff, then book — so two requests never wait for each other.
/// </summary>
public static class BorrowingLocks
{
    /// <summary>Loans, reservations and penalty points of one staff member. Shared with issuance and Staff's deactivation (BR-MEM-4).</summary>
    public static string Staff(Guid staffMemberId) => $"borrowing:staff:{staffMemberId:N}";

    /// <summary>The reservation queue of one book (BOR-07): positions are given one request at a time.</summary>
    public static string Book(Guid bookId) => $"borrowing:book:{bookId:N}";
}
