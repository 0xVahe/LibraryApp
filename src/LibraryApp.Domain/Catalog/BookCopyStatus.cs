namespace LibraryApp.Domain.Catalog;

/// <summary>
/// Status of a physical copy (requirements: BookCopy.Status). Stored as a number (SD-11):
/// never renumber or remove a value, only add new ones at the end.
/// </summary>
public enum BookCopyStatus
{
    Available = 1,
    OnLoan = 2,
    Reserved = 3,
    UnderRepair = 4,
    Lost = 5,
    Withdrawn = 6,
}
