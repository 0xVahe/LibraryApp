using LibraryApp.Domain.Notifications;
using LibraryApp.Infrastructure.Persistence.Seed.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Notifications;

public sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.Property(x => x.Subject).HasMaxLength(NotificationTemplate.SubjectMaxLength).IsRequired();   // the subject of the rendered notification
        builder.Property(x => x.BodyTemplate).HasMaxLength(-1).IsRequired();                                  // nvarchar(max)

        // At most one active template per type.
        builder.HasIndex(x => x.Type)
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_NotificationTemplates_Type_Active");

        // Default template of every notification type (NOT-02), part of the model so that every environment gets them
        // through the migrations. Fixed values only (ids and texts come from the constant seed data), otherwise the
        // model would change on every build (model-sync). Anonymous objects because the entity has private setters.
        builder.HasData(NotificationTemplateSeedData.Templates.Select(template => new
        {
            template.Id,
            template.Type,
            template.Subject,
            BodyTemplate = template.Body,
            IsActive = true,
        }));
    }
}
