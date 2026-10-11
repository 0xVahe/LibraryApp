using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Events;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>Subscribes the Notifications module to loan-returned events (NOT-01).</summary>
public sealed class LoanReturnedEventHandler : IEventHandler<LoanReturned>
{
    public Task HandleAsync(LoanReturned domainEvent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
