using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>
/// Creates the DueDateCalendarItem notification with the new due date when a loan is renewed (NOT-01). The .ics file
/// itself (NOT-07) is not attached here: emails cannot carry attachments yet.
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

        await creator.CreateAsync(
            NotificationType.DueDateCalendarItem, domainEvent.StaffMemberId, domainEvent.EventId, Channels, values,
            domainEvent.LoanId, ct: ct);
    }
}
