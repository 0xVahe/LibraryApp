using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Events;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>Subscribes the Notifications module to staff-status changes (NOT-01).</summary>
public sealed class StaffStatusChangedEventHandler : IEventHandler<StaffStatusChanged>
{
    public Task HandleAsync(StaffStatusChanged domainEvent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
