using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Events;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>Subscribes the Notifications module to staff enrolment events (NOT-01).</summary>
public sealed class StaffEnrolledEventHandler : IEventHandler<StaffEnrolled>
{
    public Task HandleAsync(StaffEnrolled domainEvent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
