using System.Data;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LibraryApp.Infrastructure.Persistence;

/// <summary>
/// <c>sp_getapplock</c> with owner "Transaction": the lock is released automatically when the transaction ends, and it
/// works across several API instances (unlike <c>lock</c> in C#). Not acquired within 5 seconds → <c>resource_busy</c> (409).
/// </summary>
public sealed class SqlServerApplicationLock(LibraryDbContext db) : IApplicationLock
{
    private const int TimeoutMilliseconds = 5000;
    private const int MaxResourceLength = 255;   // sp_getapplock limit

    public async Task AcquireAsync(string resource, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        if (resource.Length > MaxResourceLength)
            throw new ArgumentException($"A lock name has at most {MaxResourceLength} characters.", nameof(resource));

        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("An application lock needs an active transaction (use IUnitOfWork).");

        await using var command = (SqlCommand)db.Database.GetDbConnection().CreateCommand();
        command.Transaction = (SqlTransaction)transaction.GetDbTransaction();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add(new SqlParameter("@Resource", resource));
        command.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
        command.Parameters.Add(new SqlParameter("@LockOwner", "Transaction"));
        command.Parameters.Add(new SqlParameter("@LockTimeout", TimeoutMilliseconds));
        var returnValue = new SqlParameter { Direction = ParameterDirection.ReturnValue, SqlDbType = SqlDbType.Int };
        command.Parameters.Add(returnValue);

        await command.ExecuteNonQueryAsync(ct);

        if ((int)returnValue.Value < 0)   // -1 timeout, -2 cancelled, -3 deadlock victim, -999 error
            throw new DomainException(CommonErrors.ResourceBusy);
    }
}
