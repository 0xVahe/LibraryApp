using LibraryApp.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Messaging;

public sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        // One row per event and handler: a second insert for the same pair fails, so a handler never runs twice.
        builder.HasKey(x => new { x.EventId, x.Handler });

        builder.Property(x => x.Handler).HasMaxLength(ProcessedEvent.HandlerMaxLength);

        builder.HasIndex(x => x.ProcessedAtUtc).HasDatabaseName("IX_ProcessedEvents_ProcessedAtUtc");
    }
}
