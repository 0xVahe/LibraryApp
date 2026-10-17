using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Catalog;

/// <summary>A staff member's purchase suggestion (CAT-10). Approval never creates catalog stock.</summary>
public sealed class BookSuggestion : Entity
{
    public const int ReasonMaxLength = 1000;
    public const int ReviewCommentMaxLength = 1000;

    private BookSuggestion() { } // EF Core

    public BookSuggestion(Guid id, Guid staffMemberId, string title, string authorName,
        string? isbn, string reason, DateTime createdAtUtc) : base(id)
    {
        if (staffMemberId == Guid.Empty)
            throw new DomainException(CatalogErrors.SuggestionStaffIdentityRequired);
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException(CatalogErrors.BookTitleRequired);
        if (string.IsNullOrWhiteSpace(authorName))
            throw new DomainException(CatalogErrors.AuthorNameRequired);
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(CatalogErrors.SuggestionReasonRequired);

        StaffMemberId = staffMemberId;
        Title = title.Trim();
        AuthorName = authorName.Trim();
        Isbn = string.IsNullOrWhiteSpace(isbn) ? null : isbn.Trim();
        Reason = reason.Trim();
        Status = BookSuggestionStatus.Pending;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid StaffMemberId { get; private set; }
    public string Title { get; private set; } = null!;
    public string AuthorName { get; private set; } = null!;
    public string? Isbn { get; private set; }
    public string Reason { get; private set; } = null!;
    public BookSuggestionStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public string? ReviewComment { get; private set; }

    public void Approve(Guid reviewedByUserId, DateTime reviewedAtUtc)
    {
        RequirePending();
        RecordReview(BookSuggestionStatus.Approved, reviewedByUserId, reviewedAtUtc, null);
    }

    public void Reject(Guid reviewedByUserId, DateTime reviewedAtUtc, string? comment)
    {
        RequirePending();
        if (string.IsNullOrWhiteSpace(comment))
            throw new DomainException(CatalogErrors.SuggestionRejectionCommentRequired);
        RecordReview(BookSuggestionStatus.Rejected, reviewedByUserId, reviewedAtUtc, comment.Trim());
    }

    private void RequirePending()
    {
        if (Status != BookSuggestionStatus.Pending)
            throw new DomainException(CatalogErrors.SuggestionAlreadyReviewed);
    }

    private void RecordReview(BookSuggestionStatus status, Guid userId, DateTime atUtc, string? comment)
    {
        if (userId == Guid.Empty)
            throw new DomainException(CatalogErrors.SuggestionReviewerRequired);
        Status = status;
        ReviewedByUserId = userId;
        ReviewedAtUtc = atUtc;
        ReviewComment = comment;
    }
}
