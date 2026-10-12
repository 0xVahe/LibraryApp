using LibraryApp.Application.Abstractions.Common;

namespace LibraryApp.Application.Tests.Catalog;

/// <summary>Runs the operation directly, counts how many transactions were opened and tells whether one is running.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Transactions { get; private set; }

    public bool Active { get; private set; }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        Transactions++;
        Active = true;
        try
        {
            return await operation(ct);
        }
        finally
        {
            Active = false;
        }
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
        ExecuteInTransactionAsync(async token =>
        {
            await operation(token);
            return true;
        }, ct);
}

internal sealed class FakeIdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.NewGuid();
}
