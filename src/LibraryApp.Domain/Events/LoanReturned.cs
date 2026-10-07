using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Events;

/// <summary>A loan was returned (BOR-03). Publisher: Borrowing. Consumers: Notifications (ReturnReceipt), Reporting.</summary>
public sealed record LoanReturned(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid LoanId,
    Guid StaffMemberId,
    Guid BookCopyId,
    Guid BookId,
    DateTime ReturnedAtUtc,
    int OverdueWorkingDays) : IDomainEvent;
