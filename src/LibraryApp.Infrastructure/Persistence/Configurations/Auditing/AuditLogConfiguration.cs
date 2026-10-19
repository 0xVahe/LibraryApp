using LibraryApp.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Auditing;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(x => x.Action).HasMaxLength(AuditLog.ActionMaxLength).IsRequired();
        builder.Property(x => x.EntityName).HasMaxLength(AuditLog.EntityNameMaxLength).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(AuditLog.EntityIdMaxLength).IsRequired();

        // The history of one record, and everything in a period.
        builder.HasIndex(x => new { x.EntityName, x.EntityId }).HasDatabaseName("IX_AuditLogs_EntityName_EntityId");
        builder.HasIndex(x => x.TimestampUtc).HasDatabaseName("IX_AuditLogs_TimestampUtc");
    }
}
