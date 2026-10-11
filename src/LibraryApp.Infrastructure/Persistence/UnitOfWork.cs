using LibraryApp.Application.Abstractions.Common;
using System.Text.RegularExpressions;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence;

/// <summary>
/// One business operation = one database transaction (BR-BOR-4), with retry after a short connection failure.
/// <list type="bullet">
/// <item>Unsaved changes made before the call are refused: they would be committed by someone else's operation,
/// or lost on a retry.</item>
/// <item>A nested call is refused: a provider called inside another module's operation only changes data.</item>
/// <item>After any failure the transaction is rolled back and the context forgets what it tracked, so the next
/// attempt (or the next operation in the same request) starts from the database, not from stale objects.</item>
/// <item>A concurrent update and a unique-key violation become <see cref="DomainException"/> with
/// <see cref="CommonErrors.ConcurrencyConflict"/> and <see cref="CommonErrors.UniqueViolation"/> (409).</item>
/// <item>A foreign key violation becomes <see cref="CommonErrors.ReferenceInUse"/> when a referenced row is deleted, and
/// <see cref="CommonErrors.ReferenceMissing"/> when a row points to a missing one (409), whatever the module: another
/// request changed the data after the module's own check. A CHECK violation is not mapped: it is a defect.</item>
/// </list>
/// </summary>
public sealed partial class UnitOfWork(LibraryDbContext db) : IUnitOfWork
{
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await ExecuteInTransactionAsync<object?>(async token =>
        {
            await operation(token);
            return null;
        }, ct);
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException(
                "A business transaction is already open. Only the module that starts the use case opens it; " +
                "providers called inside it must only change data.");

        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException(
                "The context has unsaved changes made outside IUnitOfWork. Make every change inside the operation " +
                "passed to ExecuteInTransactionAsync.");

        // After a short network failure EF runs the whole delegate again on a clean context (see RunOnceAsync).
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(token => RunOnceAsync(operation, token), ct);
    }

    private async Task<T> RunOnceAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        try
        {
            var result = await operation(ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            db.ChangeTracker.Clear();
            throw new DomainException(CommonErrors.ConcurrencyConflict, ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            throw new DomainException(CommonErrors.UniqueViolation, ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 } sql && IsForeignKey(sql))
        {
            bool deleting = ex.Entries.Any(entry => entry.State == EntityState.Deleted);
            db.ChangeTracker.Clear();
            throw new DomainException(deleting ? CommonErrors.ReferenceInUse : CommonErrors.ReferenceMissing, ex);
        }
        catch
        {
            // Rolled back on dispose; the tracked objects no longer match the database.
            db.ChangeTracker.Clear();
            throw;
        }
    }

    // SQL error 547 is raised for foreign keys and for CHECK constraints; the constraint name tells them apart
    // (naming convention SD-12: FK_..., CK_...). The name is quoted in the message whatever the server language.
    private static bool IsForeignKey(SqlException sql) => ForeignKeyName().IsMatch(sql.Message);

    [GeneratedRegex("\"FK_[^\"]+\"")]
    private static partial Regex ForeignKeyName();
}
