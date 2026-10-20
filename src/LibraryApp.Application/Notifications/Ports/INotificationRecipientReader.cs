namespace LibraryApp.Application.Notifications.Ports;

/// <summary>Who a notification goes to: the fields of a staff member the Notifications module needs. Read only.</summary>
public interface INotificationRecipientReader
{
    /// <summary>The staff member with this id, or null when there is none. Deactivated members are found too.</summary>
    Task<NotificationRecipient?> FindAsync(Guid staffMemberId, CancellationToken ct = default);
}

/// <param name="FullName">"First Last", used as {{EmployeeName}}.</param>
/// <param name="Email">Corporate email (stored in lower case).</param>
public sealed record NotificationRecipient(Guid StaffMemberId, string FullName, string Email);
