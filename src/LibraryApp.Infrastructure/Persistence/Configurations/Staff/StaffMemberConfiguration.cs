using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Staff;

public sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.Property(x => x.EmployeeId)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.FirstName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.LastName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.CorporateEmail)
            .IsRequired();

        builder.Property(x => x.Position)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(x => x.EmployeeId)
            .IsUnique()
            .HasDatabaseName("UX_StaffMembers_EmployeeId");

        builder.HasIndex(x => x.CorporateEmail)
            .IsUnique()
            .HasDatabaseName("UX_StaffMembers_CorporateEmail");

        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasDatabaseName("UX_StaffMembers_UserId");

        builder.HasIndex(x => x.DepartmentId)
            .HasDatabaseName("IX_StaffMembers_DepartmentId");

        builder.HasIndex(x => x.StaffCategoryId)
            .HasDatabaseName("IX_StaffMembers_StaffCategoryId");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("IX_StaffMembers_Status");

        builder.HasOne<Department>()
            .WithMany()
            .HasForeignKey(x => x.DepartmentId);

        builder.HasOne<StaffCategory>()
            .WithMany()
            .HasForeignKey(x => x.StaffCategoryId);

        builder.HasOne<AppUser>()
            .WithOne()
            .HasForeignKey<StaffMember>(x => x.UserId);
    }
}