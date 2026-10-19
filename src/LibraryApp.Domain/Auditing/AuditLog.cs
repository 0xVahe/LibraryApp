using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Auditing;

/// <summary>
/// One recorded change (requirements document: AuditLog — Id, UserId, Action, Entity, EntityId, Timestamp; owner
/// "Shared"; NFR-14). <see cref="UserId"/> is the Identity user id (X-09); null when no user was signed in, for example a
/// background job or the seed ("system").
/// </summary>
public sealed class AuditLog : Entity
{
    public const int ActionMaxLength = 20;
    public const int EntityNameMaxLength = 100;
    public const int EntityIdMaxLength = 64;

    private AuditLog() { }

    private AuditLog(Guid id, Guid? userId, string action, string entityName, string entityId, DateTime timestampUtc)
        : base(id)
    {
        UserId = userId;
        Action = action;
        EntityName = entityName;
        EntityId = entityId;
        TimestampUtc = timestampUtc;
    }

    public Guid? UserId { get; private set; }

    /// <summary>One of <see cref="AuditActions"/>.</summary>
    public string Action { get; private set; } = "";

    /// <summary>The changed entity type, for example <c>Loan</c> (the requirements document calls the field "Entity").</summary>
    public string EntityName { get; private set; } = "";

    public string EntityId { get; private set; } = "";

    public DateTime TimestampUtc { get; private set; }

    public static AuditLog Record(Guid id, Guid? userId, string action, string entityName, string entityId, DateTime timestampUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        if (timestampUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The timestamp must be UTC (SD-03).", nameof(timestampUtc));

        return new AuditLog(id, userId, action, entityName, entityId, timestampUtc);
    }
}

/// <summary>Values of <see cref="AuditLog.Action"/>.</summary>
public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
}
