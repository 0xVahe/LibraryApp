using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Borrowing;

public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        // Concurrency token: two changes of the same reservation at the same time give a 409 (G3-02).
        builder.Property(x => x.RowVersion).IsRowVersion();

        // References to other modules: ids and foreign keys only, no navigation properties (SD-13).
        builder.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId);

        // One active reservation per member and book (G3-04). ReservationStatus: Waiting = 1, ReadyForPickup = 2.
        builder.HasIndex(x => new { x.StaffMemberId, x.BookId })
            .IsUnique()
            .HasFilter("[Status] IN (1, 2)")
            .HasDatabaseName("UX_Reservations_Active_StaffMemberId_BookId");

        // FIFO queue of one book.
        builder.HasIndex(x => new { x.BookId, x.Status, x.CreatedAtUtc })
            .HasDatabaseName("IX_Reservations_BookId_Status_CreatedAtUtc");

        // Active reservations of a member.
        builder.HasIndex(x => new { x.StaffMemberId, x.Status })
            .HasDatabaseName("IX_Reservations_StaffMemberId_Status");

        // QueuePosition is 1..n while Waiting and 0 otherwise (G3-03).
        // Keep CHECK SQL on one line: a multi-line string carries the line endings of the checkout into the model.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Reservations_QueuePosition",
            "([Status] = 1 AND [QueuePosition] >= 1) OR ([Status] <> 1 AND [QueuePosition] = 0)"));
    }
}
