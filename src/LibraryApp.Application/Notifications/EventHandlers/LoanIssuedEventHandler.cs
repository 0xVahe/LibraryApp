using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Events;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>Subscribes the Notifications module to loan-issued events (NOT-01).</summary>
public sealed class LoanIssuedEventHandler : IEventHandler<LoanIssued>
{
    public Task HandleAsync(LoanIssued domainEvent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
