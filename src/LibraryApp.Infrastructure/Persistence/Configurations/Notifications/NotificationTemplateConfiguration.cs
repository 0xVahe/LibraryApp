using LibraryApp.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Notifications;

public sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.Property(x => x.Subject).HasMaxLength(NotificationTemplate.SubjectMaxLength).IsRequired();   // G4-07
        builder.Property(x => x.BodyTemplate).HasMaxLength(-1).IsRequired();                                  // nvarchar(max)

        // G4-08: at most one active template per type.
        builder.HasIndex(x => x.Type)
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_NotificationTemplates_Type_Active");
    }
}
