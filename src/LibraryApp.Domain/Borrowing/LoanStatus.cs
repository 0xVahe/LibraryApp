namespace LibraryApp.Domain.Borrowing;

/// <summary>
/// Represents the lifecycle state of a loan.
/// </summary>
public enum LoanStatus
{
    /// <summary>
    /// The loan is open and has not been marked overdue.
    /// </summary>
    Active = 1,

    /// <summary>
    /// The loan is open and has been marked overdue.
    /// </summary>
    Overdue = 2,

    /// <summary>
    /// The physical copy was returned and the loan is closed.
    /// </summary>
    Returned = 3,

    /// <summary>
    /// The physical copy was declared lost and the loan is closed.
    /// </summary>
    Lost = 4
}