namespace LibraryApp.Domain.Common;

public interface IDomainEvent
{
    Guid EventId { get; }

    DateTime OccurredAtUtc { get; }
}
