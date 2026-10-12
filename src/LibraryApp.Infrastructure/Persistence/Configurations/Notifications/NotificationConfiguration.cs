using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Notifications;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(x => x.Subject).HasMaxLength(Notification.SubjectMaxLength).IsRequired();   // G4-07
        builder.Property(x => x.Body).HasMaxLength(-1).IsRequired();                                  // nvarchar(max)
        builder.Property(x => x.RecipientEmail).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(Notification.LastErrorMaxLength);

        // References to other modules: ids and foreign keys only, no navigation properties (SD-13).
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId);
        builder.HasOne<Loan>().WithMany().HasForeignKey(x => x.LoanId);

        // G4-09: an event delivered twice creates nothing new; one event may create several types
        // (e.g. LoanIssued -> LoanConfirmation + DueDateCalendarItem), one row per channel (G4-05).
        builder.HasIndex(x => new { x.EventId, x.Type, x.Channel })
            .IsUnique()
            .HasFilter("[EventId] IS NOT NULL")
            .HasDatabaseName("UX_Notifications_EventId_Type_Channel");

        // NOT-06 / G4-02: never the same type for the same loan twice on one (Yerevan) day.
        builder.HasIndex(x => new { x.LoanId, x.Type, x.Channel, x.LocalDate })
            .IsUnique()
            .HasFilter("[LoanId] IS NOT NULL")
            .HasDatabaseName("UX_Notifications_LoanId_Type_Channel_LocalDate");

        // NOT-04: a staff member's in-app list, unread first.
        builder.HasIndex(x => new { x.StaffMemberId, x.Channel, x.ReadAtUtc })
            .HasDatabaseName("IX_Notifications_StaffMemberId_Channel_ReadAtUtc");

        // NOT-08: retry job finds failed rows that are due.
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc })
            .HasDatabaseName("IX_Notifications_Status_NextAttemptAtUtc");

        builder.ToTable(t => t.HasCheckConstraint("CK_Notifications_Attempts_NonNegative", "[Attempts] >= 0"));
    }
}
