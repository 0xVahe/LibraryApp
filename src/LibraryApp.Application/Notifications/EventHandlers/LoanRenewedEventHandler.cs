using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Events;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>Subscribes the Notifications module to loan-renewed events (NOT-01).</summary>
public sealed class LoanRenewedEventHandler : IEventHandler<LoanRenewed>
{
    public Task HandleAsync(LoanRenewed domainEvent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
