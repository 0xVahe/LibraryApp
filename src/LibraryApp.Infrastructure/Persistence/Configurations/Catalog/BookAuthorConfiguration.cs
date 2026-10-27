using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class BookAuthorConfiguration : IEntityTypeConfiguration<BookAuthor>
{
    public void Configure(EntityTypeBuilder<BookAuthor> builder)
    {
        // Link table: composite key, no Id.
        builder.HasKey(x => new { x.BookId, x.AuthorId });

        // The link rows belong to the book: deleting a book removes them (same aggregate).
        builder.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);

        // An author who still has books cannot be deleted (platform default: Restrict).
        builder.HasOne<Author>().WithMany().HasForeignKey(x => x.AuthorId);
    }
}
