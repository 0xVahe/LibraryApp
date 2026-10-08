using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Events;

/// <summary>A staff member was enrolled in the library (MEM-01). Publisher: Staff. Consumers: Notifications (Welcome).</summary>
public sealed record StaffEnrolled(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid StaffMemberId) : IDomainEvent;
