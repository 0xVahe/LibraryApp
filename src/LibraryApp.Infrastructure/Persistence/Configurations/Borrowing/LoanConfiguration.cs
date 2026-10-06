using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Borrowing;

/// <summary>
/// Configures the Borrowing Loan entity.
/// </summary>
public sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    /// <summary>
    /// Configures columns, relationships, indexes, and constraints for Loan.
    /// </summary>
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        // Entity.Id comes from the shared Entity base class.
        // The application creates it through IIdGenerator.
        builder.HasKey(loan => loan.Id);

        builder.Property(loan => loan.Id)
            .ValueGeneratedNever();

        builder.Property(loan => loan.BookCopyId)
            .IsRequired();

        builder.Property(loan => loan.StaffMemberId)
            .IsRequired();

        builder.Property(loan => loan.IssuedAtUtc)
            .IsRequired();

        builder.Property(loan => loan.DueAtUtc)
            .IsRequired();

        builder.Property(loan => loan.ReturnedAtUtc)
            .IsRequired(false);

        builder.Property(loan => loan.RenewalCount)
            .IsRequired();

        // The platform stores enums as integers by default.
        builder.Property(loan => loan.Status)
            .IsRequired();

        // SQL Server manages this concurrency value.
        builder.Property(loan => loan.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // Replace these namespaces if your project uses different ones.
        builder.HasOne<LibraryApp.Domain.Catalog.BookCopy>()
            .WithMany()
            .HasForeignKey(loan => loan.BookCopyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LibraryApp.Domain.Staff.StaffMember>()
            .WithMany()
            .HasForeignKey(loan => loan.StaffMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        // Helps retrieve a member's Active and Overdue loans.
        builder.HasIndex(loan => new
        {
            loan.StaffMemberId,
            loan.Status
        })
        .HasDatabaseName("IX_Loans_StaffMemberId_Status");

        // Helps the overdue job search by status and deadline.
        builder.HasIndex(loan => new
        {
            loan.Status,
            loan.DueAtUtc
        })
        .HasDatabaseName("IX_Loans_Status_DueAtUtc");

        // One physical copy can have only one open loan.
        // LoanStatus values: Active = 1, Overdue = 2.
        builder.HasIndex(loan => loan.BookCopyId)
            .IsUnique()
            .HasFilter("[Status] IN (1, 2)")
            .HasDatabaseName("UX_Loans_Open_BookCopyId");

        // RenewalCount must never be negative.
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_Loans_RenewalCount_NonNegative",
                "[RenewalCount] >= 0");
        });
    }
}