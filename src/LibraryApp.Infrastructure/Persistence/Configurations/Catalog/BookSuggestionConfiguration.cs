using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class BookSuggestionConfiguration : IEntityTypeConfiguration<BookSuggestion>
{
    public void Configure(EntityTypeBuilder<BookSuggestion> builder)
    {
        builder.ToTable("BookSuggestions");
        builder.Property(x => x.Title).HasMaxLength(Book.TitleMaxLength).IsRequired();
        builder.Property(x => x.AuthorName).HasMaxLength(Author.FullNameMaxLength).IsRequired();
        builder.Property(x => x.Isbn).HasMaxLength(Book.IsbnLength);
        builder.Property(x => x.Reason).HasMaxLength(BookSuggestion.ReasonMaxLength).IsRequired();
        builder.Property(x => x.ReviewComment).HasMaxLength(BookSuggestion.ReviewCommentMaxLength);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).IsRequired();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId);
        builder.HasIndex(x => new { x.StaffMemberId, x.CreatedAtUtc, x.Id })
            .HasDatabaseName("IX_BookSuggestions_StaffMemberId_CreatedAtUtc_Id");
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc, x.Id })
            .HasDatabaseName("IX_BookSuggestions_Status_CreatedAtUtc_Id");
    }
}
