using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Notifications.Ports;

namespace LibraryApp.Application.Notifications.Services;

/// <summary>
/// Who a notification goes to, from the Staff contract (<see cref="IStaffMemberService.GetContactsAsync"/>, X-08): Notifications
/// never reads the staff tables itself. A Deactivated member is found too, because a notice can arrive after the deactivation.
/// </summary>
public sealed class StaffContactRecipientReader(IStaffMemberService staff) : INotificationRecipientReader
{
    public async Task<NotificationRecipient?> FindAsync(Guid staffMemberId, CancellationToken ct = default)
    {
        IReadOnlyDictionary<Guid, StaffContact> contacts = await staff.GetContactsAsync([staffMemberId], ct);

        return contacts.TryGetValue(staffMemberId, out StaffContact? contact)
            ? new NotificationRecipient(contact.StaffMemberId, contact.FullName, contact.Email, contact.LineManagerEmail)
            : null;
    }
}
