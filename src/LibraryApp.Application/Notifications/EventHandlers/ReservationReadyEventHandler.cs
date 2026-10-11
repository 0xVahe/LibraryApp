using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Events;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>Subscribes the Notifications module to reservation-ready events (NOT-01).</summary>
public sealed class ReservationReadyEventHandler : IEventHandler<ReservationReady>
{
    public Task HandleAsync(ReservationReady domainEvent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
