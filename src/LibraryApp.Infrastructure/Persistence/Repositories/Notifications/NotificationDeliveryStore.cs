using System.Data;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Notifications;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Notifications;

public sealed class NotificationDeliveryStore(LibraryDbContext db) : INotificationDeliveryStore
{
    // Same claim as OutboxDispatcher: rows locked by another instance are skipped (READPAST), and the lease is kept
    // in NextAttemptAtUtc, so a claimed email is due again only when the lease has ended. A Failed email is due at its
    // retry time; with no retry left NextAttemptAtUtc is null and it is never claimed (NOT-08).
    private const string ClaimSql = """
        WITH due AS (
            SELECT TOP (@max) [Id], [NextAttemptAtUtc]
            FROM [Notifications] WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE [Channel] = @outlook
              AND (([Status] = @pending AND ([NextAttemptAtUtc] IS NULL OR [NextAttemptAtUtc] <= @now))
                OR ([Status] = @failed AND [NextAttemptAtUtc] <= @now))
            ORDER BY [CreatedAtUtc], [Id])
        UPDATE due SET [NextAttemptAtUtc] = @leaseUntil
        OUTPUT inserted.[Id] AS [Value]
        """;

    public async Task<IReadOnlyList<Notification>> ClaimDueAsync(
        DateTime nowUtc, DateTime leaseUntilUtc, int max, CancellationToken ct = default)
    {
        List<Guid> ids = await db.Database.SqlQueryRaw<Guid>(
                ClaimSql,
                new SqlParameter("@max", SqlDbType.Int) { Value = max },
                new SqlParameter("@outlook", SqlDbType.Int) { Value = (int)NotificationChannel.Outlook },
                new SqlParameter("@pending", SqlDbType.Int) { Value = (int)NotificationStatus.Pending },
                new SqlParameter("@failed", SqlDbType.Int) { Value = (int)NotificationStatus.Failed },
                new SqlParameter("@now", SqlDbType.DateTime2) { Value = nowUtc },
                new SqlParameter("@leaseUntil", SqlDbType.DateTime2) { Value = leaseUntilUtc })
            .ToListAsync(ct);

        if (ids.Count == 0)
            return [];

        return await db.Notifications
            .Where(n => ids.Contains(n.Id))
            .OrderBy(n => n.CreatedAtUtc)
            .ThenBy(n => n.Id)
            .ToListAsync(ct);
    }

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
