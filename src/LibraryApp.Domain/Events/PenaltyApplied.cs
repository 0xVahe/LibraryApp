using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Events;

/// <summary>
/// Penalty points were added (BOR-05). <see cref="ActivePointsTotal"/> is the member's total of active points after
/// this penalty. Publisher: Borrowing. Consumers: Notifications (PenaltyApplied), Reporting.
/// </summary>
public sealed record PenaltyApplied(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid PenaltyRecordId,
    Guid StaffMemberId,
    Guid LoanId,
    int Points,
    int ActivePointsTotal,
    string Reason) : IDomainEvent;
