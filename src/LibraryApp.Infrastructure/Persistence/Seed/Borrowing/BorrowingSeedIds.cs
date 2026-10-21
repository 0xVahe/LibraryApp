namespace LibraryApp.Infrastructure.Persistence.Seed.Borrowing;

/// <summary>
/// Constant ids of the Borrowing seed. Pattern 30000000-0000-0000-&lt;kind&gt;-&lt;number&gt; (3 = Borrowing module):
/// kind 0001 loans, 0002 reservations, 0003 penalty records. They never change.
/// </summary>
public static class BorrowingSeedIds
{
    public static Guid Loan(int number) => new($"30000000-0000-0000-0001-{number:D12}");

    public static Guid Reservation(int number) => new($"30000000-0000-0000-0002-{number:D12}");

    public static Guid Penalty(int number) => new($"30000000-0000-0000-0003-{number:D12}");
}
