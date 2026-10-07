using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Domain.Events;

/// <summary>
/// The library standing of a staff member changed. Published by the module that initiated the change (X-02):
/// Borrowing for penalty-driven changes (BR-BOR-6), Staff for administrative ones. <see cref="EffectiveUntilUtc"/> is
/// the end of a suspension, otherwise null. Consumers: Notifications (AccessRestricted / Suspended, line manager).
/// </summary>
public sealed record StaffStatusChanged(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid StaffMemberId,
    StaffMemberStatus OldStatus,
    StaffMemberStatus NewStatus,
    string Reason,
    DateTime? EffectiveUntilUtc) : IDomainEvent;
