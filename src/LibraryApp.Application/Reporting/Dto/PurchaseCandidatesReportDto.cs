namespace LibraryApp.Application.Reporting.Dto;

/// <summary>
/// REP-07: the books with the longest reservation queues (Waiting reservations), longest first; at most
/// <c>Reporting:PurchaseCandidatesLimit</c>.
/// </summary>
public sealed record PurchaseCandidatesReportDto(
    DateTime GeneratedAtUtc,
    IReadOnlyList<PurchaseCandidateItemDto> Books);

/// <param name="Copies">The copies the library still has: every copy except Lost and Withdrawn ones.</param>
public sealed record PurchaseCandidateItemDto(
    Guid BookId,
    string Title,
    string Isbn,
    string Category,
    int WaitingReservations,
    int Copies);
