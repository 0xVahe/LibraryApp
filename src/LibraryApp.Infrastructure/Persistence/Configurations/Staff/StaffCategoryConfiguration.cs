using LibraryApp.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Staff;

public sealed class StaffCategoryConfiguration : IEntityTypeConfiguration<StaffCategory>
{
    public void Configure(EntityTypeBuilder<StaffCategory> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => x.Name).IsUnique().HasDatabaseName("UX_StaffCategories_Name");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_StaffCategories_MaxLoansPositive", "[MaxLoans] > 0");
            t.HasCheckConstraint("CK_StaffCategories_LoanDaysPositive", "[LoanDays] > 0");
            t.HasCheckConstraint("CK_StaffCategories_MaxRenewalsNotNegative", "[MaxRenewals] >= 0");
        });
    }
}