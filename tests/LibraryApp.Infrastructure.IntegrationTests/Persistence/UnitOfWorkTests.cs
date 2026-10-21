using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// A real SQL Server proves the transaction behaviour. No module has a table yet, so the fixture creates one probe
/// table with plain SQL; the operations write to it with plain SQL inside the business transaction.
/// (Unique-violation and concurrency mapping need SaveChanges on a mapped entity: they are tested with the first table.)
/// </summary>
public sealed class UnitOfWorkDatabase : IAsyncLifetime
{
    public string ConnectionString { get; } = TestDatabase.NewDatabase();

    public LibraryDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(ConnectionString);
        return new LibraryDbContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE dbo.UnitOfWorkProbe (Id uniqueidentifier NOT NULL PRIMARY KEY)");
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }
}

public class UnitOfWorkTests(UnitOfWorkDatabase database) : IClassFixture<UnitOfWorkDatabase>
{
    [Fact]
    public async Task Commits_the_operation_and_returns_its_result()
    {
        var id = Guid.NewGuid();
        await using var db = database.NewContext();

        var result = await new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
        {
            await Insert(db, id, ct);
            return 42;
        });

        Assert.Equal(42, result);
        Assert.True(await Exists(id));
    }

    [Fact]
    public async Task Rolls_back_everything_and_rethrows_the_same_exception_when_the_operation_fails()
    {
        var id = Guid.NewGuid();
        var failure = new InvalidOperationException("business failure");
        await using var db = database.NewContext();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
        {
            await Insert(db, id, ct);
            throw failure;
        }));

        Assert.Same(failure, thrown);
        Assert.False(await Exists(id));
    }

    [Fact]
    public async Task A_business_error_reaches_the_caller_unchanged_and_nothing_is_saved()
    {
        var id = Guid.NewGuid();
        var error = Error.BusinessRule("test.rule_broken", "Rule broken");
        await using var db = database.NewContext();

        var thrown = await Assert.ThrowsAsync<DomainException>(() => new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
        {
            await Insert(db, id, ct);
            throw new DomainException(error);
        }));

        Assert.Same(error, thrown.Error);
        Assert.False(await Exists(id));
    }

    [Fact]
    public async Task Refuses_a_nested_transaction_and_rolls_back_the_outer_work()
    {
        var id = Guid.NewGuid();
        await using var db = database.NewContext();
        var unitOfWork = new UnitOfWork(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await Insert(db, id, ct);
            await unitOfWork.ExecuteInTransactionAsync(_ => Task.CompletedTask, ct);
        }));

        Assert.Contains("already open", ex.Message);
        Assert.False(await Exists(id));
    }

    [Fact]
    public async Task The_same_context_can_run_the_next_operation_after_a_failure()
    {
        var id = Guid.NewGuid();
        await using var db = database.NewContext();
        var unitOfWork = new UnitOfWork(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(_ => throw new InvalidOperationException("first operation fails")));
        await unitOfWork.ExecuteInTransactionAsync(ct => Insert(db, id, ct));

        Assert.True(await Exists(id));
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task A_transient_failure_runs_the_whole_operation_again_and_keeps_only_the_successful_attempt()
    {
        var firstAttemptId = Guid.NewGuid();
        var secondAttemptId = Guid.NewGuid();
        var attempts = 0;
        await using var db = database.NewContext();

        await new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
        {
            attempts++;
            if (attempts == 1)
            {
                await Insert(db, firstAttemptId, ct);
                throw new TimeoutException("simulated network timeout");   // EF Core treats a timeout as transient
            }

            await Insert(db, secondAttemptId, ct);
        });

        Assert.Equal(2, attempts);
        Assert.False(await Exists(firstAttemptId));
        Assert.True(await Exists(secondAttemptId));
    }

    private static Task<int> Insert(LibraryDbContext db, Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.UnitOfWorkProbe (Id) VALUES ({id})", ct);

    private async Task<bool> Exists(Guid id)
    {
        await using var db = database.NewContext();
        return await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.UnitOfWorkProbe WHERE Id = {id}")
            .SingleAsync() == 1;
    }
}
