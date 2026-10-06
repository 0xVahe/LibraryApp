using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(Category.NameMaxLength).IsRequired();

        // Two-level tree. Delete is restricted (G1-04): a category with subcategories cannot be deleted.
        builder.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // The subcategories are read and filled through the backing field _children.
        builder.Navigation(x => x.Children).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
