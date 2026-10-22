using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>
/// Creates the DueDateCalendarItem notification with the new due date when a loan is renewed (NOT-01). The email carries the
/// updated calendar item (.ics, NOT-07): same UID as at the issue, higher SEQUENCE, so Outlook moves the event.
/// </summary>
public sealed class LoanRenewedEventHandler(
    INotificationCreator creator,
    ICatalogService catalog,
    IClock clock) : IEventHandler<LoanRenewed>
{
    // Channel of DueDateCalendarItem in the notification catalogue (requirements, section 9): Outlook only.
    private static readonly NotificationChannel[] Channels = [NotificationChannel.Outlook];

    public async Task HandleAsync(LoanRenewed domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ct.ThrowIfCancellationRequested();

        BookDto book = await catalog.GetBookAsync(domainEvent.BookId, ct)
            ?? throw new DomainException(NotificationErrors.BookNotFound);

        var values = new Dictionary<string, string>
        {
            [NotificationPlaceholders.BookTitle] = book.Title,
            [NotificationPlaceholders.DueDate] =
                NotificationPlaceholders.FormatDate(clock.ToLocalDate(domainEvent.NewDueAtUtc)),
        };

        string calendarItem = CalendarItemBuilder.Build(
            domainEvent.LoanId, book.Title, clock.ToLocalDate(domainEvent.NewDueAtUtc), domainEvent.RenewalCount, clock.UtcNow);

        await creator.CreateAsync(
            NotificationType.DueDateCalendarItem, domainEvent.StaffMemberId, domainEvent.EventId, Channels, values,
            domainEvent.LoanId, calendarItem: calendarItem, ct: ct);
    }
}
