using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>
/// Creates the first OverdueNotice when a loan becomes overdue (NOT-01). The repeated reminders every 5 working days and
/// the line manager in CC after 14 days (NOT-05) belong to the daily job, not to this handler.
/// </summary>
public sealed class LoanOverdueEventHandler(
    INotificationCreator creator,
    ICatalogService catalog,
    IClock clock) : IEventHandler<LoanOverdue>
{
    // Channels of OverdueNotice in the notification catalogue (requirements, section 9): Outlook + in-app.
    private static readonly NotificationChannel[] Channels = [NotificationChannel.Outlook, NotificationChannel.InApp];

    public async Task HandleAsync(LoanOverdue domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ct.ThrowIfCancellationRequested();

        BookDto book = await catalog.GetBookAsync(domainEvent.BookId, ct)
            ?? throw new DomainException(NotificationErrors.BookNotFound);

        var values = new Dictionary<string, string>
        {
            [NotificationPlaceholders.BookTitle] = book.Title,
            [NotificationPlaceholders.DueDate] =
                NotificationPlaceholders.FormatDate(clock.ToLocalDate(domainEvent.DueAtUtc)),
        };

        await creator.CreateAsync(
            NotificationType.OverdueNotice, domainEvent.StaffMemberId, domainEvent.EventId, Channels, values,
            domainEvent.LoanId, ct);
    }
}
