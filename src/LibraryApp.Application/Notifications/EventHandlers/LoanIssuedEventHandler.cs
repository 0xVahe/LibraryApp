using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>
/// Creates the LoanConfirmation and the DueDateCalendarItem notifications when a loan is issued (NOT-01). Both Outlook
/// emails carry the calendar item of the due date (.ics, NOT-07); it has the UID of the loan, so a renewal updates it.
/// </summary>
public sealed class LoanIssuedEventHandler(
    INotificationCreator creator,
    ICatalogService catalog,
    IClock clock) : IEventHandler<LoanIssued>
{
    // Channels in the notification catalogue (requirements, section 9): LoanConfirmation is Outlook + in-app,
    // DueDateCalendarItem is Outlook only.
    private static readonly NotificationChannel[] ConfirmationChannels = [NotificationChannel.Outlook, NotificationChannel.InApp];
    private static readonly NotificationChannel[] CalendarChannels = [NotificationChannel.Outlook];

    public async Task HandleAsync(LoanIssued domainEvent, CancellationToken ct)
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

        // One event, two types: the unique index (EventId, Type, Channel) allows it, and both are stored in the same
        // transaction, so a repeated event creates each of them at most once.
        string calendarItem = CalendarItemBuilder.Build(
            domainEvent.LoanId, book.Title, clock.ToLocalDate(domainEvent.DueAtUtc), sequence: 0, clock.UtcNow);

        await creator.CreateAsync(
            NotificationType.LoanConfirmation, domainEvent.StaffMemberId, domainEvent.EventId, ConfirmationChannels, values,
            domainEvent.LoanId, calendarItem: calendarItem, ct: ct);

        await creator.CreateAsync(
            NotificationType.DueDateCalendarItem, domainEvent.StaffMemberId, domainEvent.EventId, CalendarChannels, values,
            domainEvent.LoanId, calendarItem: calendarItem, ct: ct);
    }
}
