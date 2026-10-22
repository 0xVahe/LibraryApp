using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Notifications;

/// <summary>EF Core read access to the notification templates.</summary>
public sealed class NotificationTemplateRepository(LibraryDbContext db) : INotificationTemplateRepository
{
    public Task<NotificationTemplate?> FindActiveAsync(NotificationType type, CancellationToken ct = default) =>
        db.NotificationTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Type == type && t.IsActive, ct);
}
