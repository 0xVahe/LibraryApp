namespace LibraryApp.Application.Abstractions.Borrowing;

/// <summary>Shared with issuance: acquire the staff lock before reading eligibility or open loans.</summary>
public static class BorrowingLocks
{
    public static string Staff(Guid staffMemberId) => $"borrowing:staff:{staffMemberId:N}";
}
