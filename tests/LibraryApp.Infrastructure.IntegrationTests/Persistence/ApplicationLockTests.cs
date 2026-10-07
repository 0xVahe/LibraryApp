using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

/// <summary>sp_getapplock on a real SQL Server: only inside a transaction, exclusive, released at the end of the transaction.</summary>
public class ApplicationLockTests(UnitOfWorkDatabase database) : IClassFixture<UnitOfWorkDatabase>
{
    [Fact]
    public async Task A_lock_outside_a_transaction_is_refused()
    {
        await using var db = database.NewContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqlServerApplicationLock(db).AcquireAsync("borrowing:staff:1"));
    }

    [Fact]
    public async Task A_second_transaction_waits_and_gets_resource_busy()
    {
        var resource = $"borrowing:staff:{Guid.NewGuid():N}";
        await using var first = database.NewContext();
        await using var second = database.NewContext();

        await new UnitOfWork(first).ExecuteInTransactionAsync(async ct =>
        {
            await new SqlServerApplicationLock(first).AcquireAsync(resource, ct);

            var ex = await Assert.ThrowsAsync<DomainException>(() =>
                new UnitOfWork(second).ExecuteInTransactionAsync(token => new SqlServerApplicationLock(second).AcquireAsync(resource, token)));
            Assert.Equal("resource_busy", ex.Error.Code);
        });
    }

    [Fact]
    public async Task The_lock_is_released_when_the_transaction_ends()
    {
        var resource = $"borrowing:staff:{Guid.NewGuid():N}";
        await using var first = database.NewContext();
        await using var second = database.NewContext();

        await new UnitOfWork(first).ExecuteInTransactionAsync(ct => new SqlServerApplicationLock(first).AcquireAsync(resource, ct));
        await new UnitOfWork(second).ExecuteInTransactionAsync(ct => new SqlServerApplicationLock(second).AcquireAsync(resource, ct));
    }

    [Fact]
    public async Task A_too_long_name_is_refused()
    {
        await using var db = database.NewContext();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new UnitOfWork(db).ExecuteInTransactionAsync(ct => new SqlServerApplicationLock(db).AcquireAsync(new string('x', 256), ct)));
    }
}
