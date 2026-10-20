using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>Creates the ReturnReceipt notification when a loan is returned (NOT-01).</summary>
public sealed class LoanReturnedEventHandler(
    INotificationCreator creator,
    ICatalogService catalog,
    IClock clock) : IEventHandler<LoanReturned>
{
    // Channel of ReturnReceipt in the notification catalogue (requirements, section 9): in-app only.
    private static readonly NotificationChannel[] Channels = [NotificationChannel.InApp];

    public async Task HandleAsync(LoanReturned domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ct.ThrowIfCancellationRequested();

        BookDto book = await catalog.GetBookAsync(domainEvent.BookId, ct)
            ?? throw new DomainException(NotificationErrors.BookNotFound);

        var values = new Dictionary<string, string>
        {
            [NotificationPlaceholders.BookTitle] = book.Title,
            [NotificationPlaceholders.ReturnedDate] =
                NotificationPlaceholders.FormatDate(clock.ToLocalDate(domainEvent.ReturnedAtUtc)),
        };

        await creator.CreateAsync(
            NotificationType.ReturnReceipt, domainEvent.StaffMemberId, domainEvent.EventId, Channels, values,
            domainEvent.LoanId, ct);
    }
}
