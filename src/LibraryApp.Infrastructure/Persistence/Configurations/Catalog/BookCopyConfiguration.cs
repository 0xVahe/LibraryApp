using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class BookCopyConfiguration : IEntityTypeConfiguration<BookCopy>
{
    public void Configure(EntityTypeBuilder<BookCopy> builder)
    {
        builder.Property(x => x.Barcode).HasMaxLength(BookCopy.BarcodeMaxLength).IsRequired();
        builder.Property(x => x.ShelfLocation).HasMaxLength(BookCopy.ShelfLocationMaxLength);

        // Status and Condition are stored as int (SD-11): no conversion needed.

        // Concurrency token: a second change of the same copy fails and becomes a 409.
        builder.Property(x => x.RowVersion).IsRowVersion();

        // BR-CAT-2: the barcode is unique across ALL copies, including retired ones (no filter).
        builder.HasIndex(x => x.Barcode)
            .IsUnique()
            .HasDatabaseName("UX_BookCopies_Barcode");

        // "Find a free copy of this book" (GetAvailableCopyAsync) and "copies of a book by status".
        builder.HasIndex(x => new { x.BookId, x.Status })
            .HasDatabaseName("IX_BookCopies_BookId_Status");

        // Delete is restricted: a book with copies is soft-deleted, never removed from the table.
        builder.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId).IsRequired();
    }
}
