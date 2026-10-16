using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Borrowing;

public sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        // Concurrency token: two changes of the same loan at the same time give a 409 (G3-02).
        builder.Property(x => x.RowVersion).IsRowVersion();

        // Optional note recorded during a return.
        builder.Property(x => x.ReturnNote)
            .HasMaxLength(Loan.ReturnNoteMaxLength)
            .IsRequired(false);
        
        // References to other modules: ids and foreign keys only, no navigation properties (SD-13).
        builder.HasOne<BookCopy>().WithMany().HasForeignKey(x => x.BookCopyId);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId);

        // One open loan per copy (G3-04). LoanStatus: Active = 1, Overdue = 2.
        builder.HasIndex(x => x.BookCopyId)
            .IsUnique()
            .HasFilter("[Status] IN (1, 2)")
            .HasDatabaseName("UX_Loans_Open_BookCopyId");

        // Open loans of a member (loan limit, history).
        builder.HasIndex(x => new { x.StaffMemberId, x.Status })
            .HasDatabaseName("IX_Loans_StaffMemberId_Status");

        // Daily job: active loans past their due date.
        builder.HasIndex(x => new { x.Status, x.DueAtUtc })
            .HasDatabaseName("IX_Loans_Status_DueAtUtc");

        builder.ToTable(t => t.HasCheckConstraint("CK_Loans_RenewalCount_NonNegative", "[RenewalCount] >= 0"));
    }
}
