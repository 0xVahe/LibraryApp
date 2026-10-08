using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Events;

/// <summary>
/// A loan became overdue (daily job, BOR-06). Published once per loan; repeated reminders are produced by the
/// Notifications job. Publisher: Borrowing. Consumers: Notifications (OverdueNotice), Reporting.
/// </summary>
public sealed record LoanOverdue(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid LoanId,
    Guid StaffMemberId,
    Guid BookId,
    DateTime DueAtUtc) : IDomainEvent;
