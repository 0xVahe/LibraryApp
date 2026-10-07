using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Events;

/// <summary>A loan was issued (BOR-01). Publisher: Borrowing. Consumers: Notifications (LoanConfirmation, .ics), Reporting.</summary>
public sealed record LoanIssued(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid LoanId,
    Guid StaffMemberId,
    Guid BookCopyId,
    Guid BookId,
    DateTime DueAtUtc) : IDomainEvent;
