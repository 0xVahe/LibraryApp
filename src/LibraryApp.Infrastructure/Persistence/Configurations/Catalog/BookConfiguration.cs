using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.Property(x => x.Title).HasMaxLength(Book.TitleMaxLength).IsRequired();
        builder.Property(x => x.Isbn).HasMaxLength(Book.IsbnLength).IsRequired();          // 13 digits (G1-02)
        builder.Property(x => x.Language).HasMaxLength(Book.LanguageLength).IsRequired();  // ISO 639-1 (G1-03)
        builder.Property(x => x.Description).HasMaxLength(-1);                              // nvarchar(max)

        // BR-CAT-1: the ISBN is unique among books that are not deleted (G1-02).
        builder.HasIndex(x => x.Isbn)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Books_Isbn");

        // Both are in the same module, so the foreign keys are written without navigation properties.
        // Delete stays restricted (platform default): a category or publisher in use cannot disappear.
        builder.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).IsRequired();
        builder.HasOne<Publisher>().WithMany().HasForeignKey(x => x.PublisherId);
    }
}
