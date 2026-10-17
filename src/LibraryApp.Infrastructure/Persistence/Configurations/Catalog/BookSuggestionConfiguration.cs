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
        // The review rules of BookSuggestion (Approve, Reject) also as CHECK constraints, a second line of defence.
        // BookSuggestionStatus: Pending = 1, Approved = 2, Rejected = 3.
        // Keep CHECK SQL on one line: a multi-line string carries the line endings of the checkout into the model.
        builder.ToTable("BookSuggestions", t =>
        {
            t.HasCheckConstraint(
                "CK_BookSuggestions_Review",
                "([Status] = 1 AND [ReviewedAtUtc] IS NULL AND [ReviewedByUserId] IS NULL AND [ReviewComment] IS NULL) OR ([Status] IN (2, 3) AND [ReviewedAtUtc] IS NOT NULL AND [ReviewedByUserId] IS NOT NULL)");
            t.HasCheckConstraint(
                "CK_BookSuggestions_RejectComment",
                "[Status] <> 3 OR ([ReviewComment] IS NOT NULL AND LEN(LTRIM(RTRIM([ReviewComment]))) > 0)");
        });
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
