using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Auditing;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LibraryApp.Infrastructure.Persistence.Auditing;

/// <summary>
/// NFR-14: every create, update and delete of books, staff members, loans and penalty records gets an
/// <see cref="AuditLog"/> row, saved by the same SaveChanges and therefore in the same transaction as the change: a
/// rolled-back change leaves no audit row. Recording here, not in the services, means no module can forget it (SD-18).
/// <para>
/// A soft delete of a book is an update (IsDeleted). The user is the "sub" claim of the request's access token; jobs,
/// event handlers, the seed and anonymous requests (login) have none and are recorded as system (UserId null).
/// </para>
/// </summary>
public sealed class AuditSaveChangesInterceptor(IHttpContextAccessor http, IClock clock, IIdGenerator ids) : SaveChangesInterceptor
{
    /// <summary>The entities NFR-14 names. A new audited entity is added here in the same pull request.</summary>
    public static readonly IReadOnlySet<Type> AuditedTypes = new HashSet<Type>
    {
        typeof(Book), typeof(StaffMember), typeof(Loan), typeof(PenaltyRecord),
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Record(DbContext? context)
    {
        if (context is null)
            return;

        // ToList: adding AuditLog rows changes the tracked entries while they are enumerated.
        var changes = context.ChangeTracker.Entries<Entity>()
            .Where(e => AuditedTypes.Contains(e.Metadata.ClrType)
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => (e.Entity, Action: ActionOf(e.State)))
            .ToList();
        if (changes.Count == 0)
            return;

        DateTime now = clock.UtcNow;
        Guid? userId = SignedInUserId();
        foreach (var (entity, action) in changes)
            context.Add(AuditLog.Record(ids.NewId(), userId, action, entity.GetType().Name, entity.Id.ToString(), now));
    }

    private Guid? SignedInUserId() =>
        http.HttpContext?.User is { Identity.IsAuthenticated: true } user
        && Guid.TryParse(user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out Guid id)
            ? id
            : null;

    private static string ActionOf(EntityState state) => state switch
    {
        EntityState.Added => AuditActions.Created,
        EntityState.Modified => AuditActions.Updated,
        _ => AuditActions.Deleted,
    };
}
