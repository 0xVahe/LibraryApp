using LibraryApp.Application.Abstractions.Common;

namespace LibraryApp.Application.Tests.Borrowing.Fakes;

public sealed class FakeUnitOfWork(FakeLoanRepository loans, InMemoryCatalogService catalog, FakeEventBus? events = null)
    : IUnitOfWork
{
    public int StartedCount { get; private set; }
    public int CommittedCount { get; private set; }
    public int RolledBackCount { get; private set; }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ct.ThrowIfCancellationRequested();

        var catalogSnapshot = catalog.Snapshot();
        StartedCount++;

        try
        {
            T result = await operation(ct);

            ct.ThrowIfCancellationRequested();
            loans.Commit();
            events?.Commit();
            CommittedCount++;

            return result;
        }
        catch
        {
            loans.Rollback();
            events?.Rollback();
            catalog.Restore(catalogSnapshot);
            RolledBackCount++;

            throw;
        }
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await ExecuteInTransactionAsync<object?>(async token =>
        {
            await operation(token);
            return null;
        }, ct);
    }
}