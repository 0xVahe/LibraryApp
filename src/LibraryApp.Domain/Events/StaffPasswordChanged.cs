using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Events;

/// <summary>A staff member changed their password (MEM-02). Publisher: Staff. Consumers: Notifications (security notice).</summary>
public sealed record StaffPasswordChanged(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid StaffMemberId) : IDomainEvent;