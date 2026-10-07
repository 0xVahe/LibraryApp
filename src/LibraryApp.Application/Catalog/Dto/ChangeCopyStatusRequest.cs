using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Dto;

/// <summary>
/// A librarian takes a copy out of circulation or brings it back from repair (CAT-04, BR-CAT-4).
/// Allowed: UnderRepair, Lost, Withdrawn, and Available only for a copy that is UnderRepair.
/// OnLoan and Reserved are set only by Borrowing.
/// </summary>
/// <param name="Status">The new status.</param>
public sealed record ChangeCopyStatusRequest(BookCopyStatus Status);
