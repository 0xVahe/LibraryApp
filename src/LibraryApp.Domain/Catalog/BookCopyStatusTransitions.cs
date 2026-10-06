namespace LibraryApp.Domain.Catalog;

/// <summary>
/// Which status a copy may move to from its current status.
/// Lost and Withdrawn are final: such a copy never returns to circulation.
/// </summary>
public static class BookCopyStatusTransitions
{
    private static readonly Dictionary<BookCopyStatus, BookCopyStatus[]> AllowedNextStatuses = new()
    {
        [BookCopyStatus.Available] = new[]
        {
            BookCopyStatus.OnLoan,
            BookCopyStatus.Reserved,
            BookCopyStatus.UnderRepair,
            BookCopyStatus.Lost,
            BookCopyStatus.Withdrawn
        },
        [BookCopyStatus.OnLoan] = new[]
        {
            BookCopyStatus.Available,    // normal return
            BookCopyStatus.Reserved,     // return while someone is waiting in the queue
            BookCopyStatus.UnderRepair,  // returned damaged
            BookCopyStatus.Lost          // marked lost (BOR-09)
        },
        [BookCopyStatus.Reserved] = new[]
        {
            BookCopyStatus.OnLoan,       // reserved copy picked up
            BookCopyStatus.Available,    // reservation expired or cancelled
            BookCopyStatus.UnderRepair,
            BookCopyStatus.Lost,
            BookCopyStatus.Withdrawn
        },
        [BookCopyStatus.UnderRepair] = new[]
        {
            BookCopyStatus.Available,    // repaired
            BookCopyStatus.Lost,
            BookCopyStatus.Withdrawn
        },
        [BookCopyStatus.Lost] = Array.Empty<BookCopyStatus>(),
        [BookCopyStatus.Withdrawn] = Array.Empty<BookCopyStatus>()
    };

    public static bool IsAllowed(BookCopyStatus currentStatus, BookCopyStatus newStatus)
    {
        BookCopyStatus[] allowed = AllowedNextStatuses[currentStatus];
        return allowed.Contains(newStatus);
    }
}
