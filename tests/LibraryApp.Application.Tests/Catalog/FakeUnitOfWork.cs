using LibraryApp.Application.Abstractions.Common;

namespace LibraryApp.Application.Tests.Catalog;

/// <summary>Runs the operation directly and counts how many transactions were opened.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Transactions { get; private set; }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        Transactions++;
        return await operation(ct);
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
    {
        Transactions++;
        await operation(ct);
    }
}

internal sealed class FakeIdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.NewGuid();
}
