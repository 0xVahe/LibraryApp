using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Events;

/// <summary>
/// Penalty points were added (BOR-05): one event per operation — a return or a declared loss — however many penalty
/// records it created. <see cref="Points"/> is the sum of its records, <see cref="Reason"/> their reasons joined with
/// "; ", <see cref="ActivePointsTotal"/> the member's total of active points after the operation. An operation without
/// points publishes nothing. Publisher: Borrowing. Consumer: Notifications (PenaltyApplied).
/// </summary>
public sealed record PenaltyApplied(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid StaffMemberId,
    Guid LoanId,
    int Points,
    int ActivePointsTotal,
    string Reason) : IDomainEvent;
