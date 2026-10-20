using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>Creates the ReservationReady notification when a reserved copy is waiting for pickup (NOT-01).</summary>
public sealed class ReservationReadyEventHandler(
    INotificationCreator creator,
    ICatalogService catalog,
    IClock clock) : IEventHandler<ReservationReady>
{
    // Channels of ReservationReady in the notification catalogue (requirements, section 9): Outlook + in-app.
    private static readonly NotificationChannel[] Channels = [NotificationChannel.Outlook, NotificationChannel.InApp];

    public async Task HandleAsync(ReservationReady domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ct.ThrowIfCancellationRequested();

        // A soft-deleted book is still returned, so an old reservation keeps resolving (CAT-07).
        BookDto book = await catalog.GetBookAsync(domainEvent.BookId, ct)
            ?? throw new DomainException(NotificationErrors.BookNotFound);

        var values = new Dictionary<string, string>
        {
            [NotificationPlaceholders.BookTitle] = book.Title,
            [NotificationPlaceholders.ReadyUntil] =
                NotificationPlaceholders.FormatDate(clock.ToLocalDate(domainEvent.ReadyUntilUtc)),
        };

        await creator.CreateAsync(
            NotificationType.ReservationReady, domainEvent.StaffMemberId, domainEvent.EventId, Channels, values, ct: ct);
    }
}
