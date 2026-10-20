using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>
/// Creates the LoanConfirmation notification when a loan is issued (NOT-01). The .ics calendar item (NOT-07,
/// DueDateCalendarItem) is not created here: emails cannot carry attachments yet.
/// </summary>
public sealed class LoanIssuedEventHandler(
    INotificationCreator creator,
    ICatalogService catalog,
    IClock clock) : IEventHandler<LoanIssued>
{
    // Channels of LoanConfirmation in the notification catalogue (requirements, section 9): Outlook + in-app.
    private static readonly NotificationChannel[] Channels = [NotificationChannel.Outlook, NotificationChannel.InApp];

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

        await creator.CreateAsync(
            NotificationType.LoanConfirmation, domainEvent.StaffMemberId, domainEvent.EventId, Channels, values,
            domainEvent.LoanId, ct);
    }
}
