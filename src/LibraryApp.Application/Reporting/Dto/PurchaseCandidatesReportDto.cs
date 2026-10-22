namespace LibraryApp.Application.Reporting.Dto;

/// <summary>
/// REP-07: the books with the longest reservation queues (Waiting reservations), longest first; a tie by title, then ISBN.
/// </summary>
/// <param name="AsOfDate">The day of the report (Asia/Yerevan): the queues as they are now.</param>
/// <param name="GeneratedAtUtc">When the report was made.</param>
/// <param name="Limit">At most this many books are listed (<c>Reporting:PurchaseCandidatesLimit</c>).</param>
/// <param name="Books">The books with at least one Waiting reservation, longest queue first.</param>
public sealed record PurchaseCandidatesReportDto(
    DateOnly AsOfDate,
    DateTime GeneratedAtUtc,
    int Limit,
    IReadOnlyList<PurchaseCandidateItemDto> Books);

/// <summary>A book with its reservation queue (REP-07). Deleted books are not listed.</summary>
/// <param name="BookId">Book id.</param>
/// <param name="Title">Book title.</param>
/// <param name="Isbn">ISBN of the book.</param>
/// <param name="Category">The category of the book.</param>
/// <param name="WaitingReservations">The reservations in the queue (Waiting); ready-for-pickup, expired and cancelled ones
/// are not in the queue.</param>
/// <param name="CopiesInCollection">The copies the library still has: every copy except Lost and Withdrawn, as in REP-05.</param>
public sealed record PurchaseCandidateItemDto(
    Guid BookId,
    string Title,
    string Isbn,
    string Category,
    int WaitingReservations,
    int CopiesInCollection);
