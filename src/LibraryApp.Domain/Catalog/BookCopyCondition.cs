namespace LibraryApp.Domain.Catalog;

/// <summary>
/// Physical condition of a copy. "Damaged" is not a condition: a damaged copy gets the status UnderRepair.
/// Stored as a number: never renumber or remove a value, only add new ones at the end.
/// </summary>
public enum BookCopyCondition
{
    New = 1,
    Good = 2,
    Fair = 3,
    Poor = 4,
}
