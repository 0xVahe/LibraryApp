using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Events;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>Subscribes the Notifications module to penalty-applied events (NOT-01).</summary>
public sealed class PenaltyAppliedEventHandler : IEventHandler<PenaltyApplied>
{
    public Task HandleAsync(PenaltyApplied domainEvent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
