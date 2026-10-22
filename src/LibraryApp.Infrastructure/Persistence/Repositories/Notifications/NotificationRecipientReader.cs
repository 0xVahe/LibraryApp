using LibraryApp.Application.Notifications.Ports;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Notifications;

/// <summary>Reads the name and corporate email of a staff member for the Notifications module (read only).</summary>
public sealed class NotificationRecipientReader(LibraryDbContext db) : INotificationRecipientReader
{
    public Task<NotificationRecipient?> FindAsync(Guid staffMemberId, CancellationToken ct = default) =>
        db.StaffMembers.AsNoTracking()
            .Where(x => x.Id == staffMemberId)
            .Select(x => new NotificationRecipient(x.Id, x.FirstName + " " + x.LastName, x.CorporateEmail, x.LineManagerEmail))
            .SingleOrDefaultAsync(ct);
}
