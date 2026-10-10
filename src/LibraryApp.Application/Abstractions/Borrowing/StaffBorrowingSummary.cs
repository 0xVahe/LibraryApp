namespace LibraryApp.Application.Abstractions.Borrowing;

/// <summary>Borrowing data of one staff member for MEM-08 "My library" (BQ-G2-11).</summary>
public sealed record StaffBorrowingSummary(
    Guid StaffMemberId,
    int ActiveLoansCount,
    int OverdueLoansCount,
    int TotalBorrowedCount);
