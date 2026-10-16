using LibraryApp.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Jobs;

public sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> builder)
    {
        builder.Property(x => x.JobName).HasMaxLength(JobRun.JobNameMaxLength).IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(JobRun.SummaryMaxLength);
        builder.Property(x => x.LastError).HasMaxLength(JobRun.ErrorMaxLength);

        // One run per job and local date: a second instance trying the same day finds this row.
        builder.HasIndex(x => new { x.JobName, x.LocalDate })
            .IsUnique()
            .HasDatabaseName("UX_JobRuns_JobName_LocalDate");

        builder.ToTable(t => t.HasCheckConstraint("CK_JobRuns_Attempts", "[Attempts] >= 1"));
    }
}
