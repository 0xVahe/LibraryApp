using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Borrowing;

public sealed class PenaltyRecordConfiguration : IEntityTypeConfiguration<PenaltyRecord>
{
    public void Configure(EntityTypeBuilder<PenaltyRecord> builder)
    {
        builder.Property(x => x.Reason).HasMaxLength(PenaltyRecord.ReasonMaxLength);
        builder.Property(x => x.WaivedReason).HasMaxLength(PenaltyRecord.ReasonMaxLength);

        // Concurrency token: a penalty waived and expired at the same time gives a 409.
        builder.Property(x => x.RowVersion).IsRowVersion();

        // A penalty belongs to one loan (same module); a loan with penalties is never deleted (Restrict, default).
        builder.HasOne<Loan>().WithMany().HasForeignKey(x => x.LoanId);

        // Reference to another module: id and foreign key only.
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId);

        // Active points of a member.
        builder.HasIndex(x => new { x.StaffMemberId, x.Status, x.ExpiresAtUtc })
            .HasDatabaseName("IX_PenaltyRecords_StaffMemberId_Status_ExpiresAtUtc");

        builder.HasIndex(x => x.LoanId).HasDatabaseName("IX_PenaltyRecords_LoanId");

        // Points 0..10 and a reason when waived. PenaltyRecordStatus: Waived = 3.
        // Keep CHECK SQL on one line: a multi-line string carries the line endings of the checkout into the model.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PenaltyRecords_Points", "[Points] BETWEEN 0 AND 10");
            t.HasCheckConstraint(
                "CK_PenaltyRecords_WaivedReason",
                "[Status] <> 3 OR ([WaivedReason] IS NOT NULL AND LEN(LTRIM(RTRIM([WaivedReason]))) > 0)");
        });
    }
}
