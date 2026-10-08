using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Events;

/// <summary>A loan was renewed (BOR-04). Publisher: Borrowing. Consumers: Notifications (updated .ics).</summary>
public sealed record LoanRenewed(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid LoanId,
    Guid StaffMemberId,
    Guid BookId,
    DateTime NewDueAtUtc,
    int RenewalCount) : IDomainEvent;
