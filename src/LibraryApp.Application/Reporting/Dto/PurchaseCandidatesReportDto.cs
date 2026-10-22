namespace LibraryApp.Application.Reporting.Dto;

/// <summary>
/// REP-07: the books with the longest reservation queues (Waiting reservations) as of <see cref="AsOfDate"/>, longest
/// first; a tie by title, then ISBN. At most <see cref="Limit"/> books (<c>Reporting:PurchaseCandidatesLimit</c>).
/// </summary>
public sealed record PurchaseCandidatesReportDto(
    DateOnly AsOfDate,
    DateTime GeneratedAtUtc,
    int Limit,
    IReadOnlyList<PurchaseCandidateItemDto> Books);

/// <param name="Copies">The copies the library still has: every copy except Lost and Withdrawn ones.</param>
public sealed record PurchaseCandidateItemDto(
    Guid BookId,
    string Title,
    string Isbn,
    string Category,
    int WaitingReservations,
    int Copies);
