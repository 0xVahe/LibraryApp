using LibraryApp.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Staff;

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();

        builder.HasIndex(x => x.Name).IsUnique().HasDatabaseName("UX_Departments_Name");
    }
}