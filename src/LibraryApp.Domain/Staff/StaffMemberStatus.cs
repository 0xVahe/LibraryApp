namespace LibraryApp.Domain.Staff;
 
/// <summary>
/// Library standing of a staff member. Values are stored as numbers: never renumber, only add at the end.
/// </summary>
public enum StaffMemberStatus
{
    /// <summary>May borrow within the limits of the staff category.</summary>
    Active = 1,
 
    /// <summary>Set by Borrowing at 5 active penalty points: 1 loan maximum, no renewals, no reservations.</summary>
    Restricted = 2,
 
    /// <summary>Set by Borrowing at 10 active penalty points: no borrowing for 30 days.</summary>
    Suspended = 3,
 
    /// <summary>The employee has left the bank. The account is kept for history.</summary>
    Deactivated = 4,
}