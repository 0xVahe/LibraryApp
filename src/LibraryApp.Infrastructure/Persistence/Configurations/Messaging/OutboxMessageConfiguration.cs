using LibraryApp.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Messaging;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        // The key is the event's EventId, given by the publisher.
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Type).HasMaxLength(OutboxMessage.TypeMaxLength);
        builder.Property(x => x.Payload).HasMaxLength(-1);
        builder.Property(x => x.LastError).HasMaxLength(OutboxMessage.LastErrorMaxLength);

        // The dispatcher reads only unprocessed messages: a small filtered index keeps polling cheap however big the table is.
        builder.HasIndex(x => new { x.OccurredAtUtc, x.Id })
            .HasFilter("[ProcessedAtUtc] IS NULL")
            .HasDatabaseName("IX_OutboxMessages_Pending");

        // Cleanup of processed messages by age.
        builder.HasIndex(x => x.ProcessedAtUtc)
            .HasFilter("[ProcessedAtUtc] IS NOT NULL")
            .HasDatabaseName("IX_OutboxMessages_ProcessedAtUtc");
    }
}
