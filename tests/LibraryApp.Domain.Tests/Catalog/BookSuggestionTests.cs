using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Tests.Catalog;

public class BookSuggestionTests
{
    private static readonly DateTime Created = new(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc);

    private static BookSuggestion New() => new(Guid.NewGuid(), Guid.NewGuid(), "  Suggested title  ",
        "  An author  ", null, "  Work reference  ", Created);

    [Fact]
    public void New_suggestion_is_pending_with_ownership_and_no_review()
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var suggestion = new BookSuggestion(id, owner, " Title ", " Author ", null, " Reason ", Created);
        Assert.Equal(id, suggestion.Id);
        Assert.Equal(owner, suggestion.StaffMemberId);
        Assert.Equal("Title", suggestion.Title);
        Assert.Equal("Author", suggestion.AuthorName);
        Assert.Equal("Reason", suggestion.Reason);
        Assert.Null(suggestion.Isbn);
        Assert.Equal(BookSuggestionStatus.Pending, suggestion.Status);
        Assert.Equal(Created, suggestion.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, suggestion.CreatedAtUtc.Kind);
        Assert.Null(suggestion.ReviewedAtUtc);
        Assert.Null(suggestion.ReviewedByUserId);
        Assert.Null(suggestion.ReviewComment);
        Assert.Equal(new[] { 1, 2, 3 }, Enum.GetValues<BookSuggestionStatus>().Select(x => (int)x));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Review_records_identity_time_and_comment(bool reject)
    {
        var suggestion = New();
        var reviewer = Guid.NewGuid();
        var at = Created.AddDays(1);
        if (reject) suggestion.Reject(reviewer, at, "  Already planned  ");
        else suggestion.Approve(reviewer, at);
        Assert.Equal(reject ? BookSuggestionStatus.Rejected : BookSuggestionStatus.Approved, suggestion.Status);
        Assert.Equal(reviewer, suggestion.ReviewedByUserId);
        Assert.Equal(at, suggestion.ReviewedAtUtc);
        Assert.Equal(DateTimeKind.Utc, suggestion.ReviewedAtUtc!.Value.Kind);
        Assert.Equal(reject ? "Already planned" : null, suggestion.ReviewComment);
        Assert.Equal(Created, suggestion.CreatedAtUtc);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Every_second_review_conflicts_and_preserves_the_first(bool firstReject, bool secondReject)
    {
        var suggestion = New();
        var reviewer = Guid.NewGuid();
        if (firstReject) suggestion.Reject(reviewer, Created.AddHours(1), "First comment");
        else suggestion.Approve(reviewer, Created.AddHours(1));
        var error = Assert.Throws<DomainException>(() =>
        {
            if (secondReject) suggestion.Reject(Guid.NewGuid(), Created.AddHours(2), "Second comment");
            else suggestion.Approve(Guid.NewGuid(), Created.AddHours(2));
        });
        Assert.Equal(CatalogErrors.SuggestionAlreadyReviewed, error.Error);
        Assert.Equal(reviewer, suggestion.ReviewedByUserId);
        Assert.Equal(Created.AddHours(1), suggestion.ReviewedAtUtc);
        Assert.Equal(firstReject ? "First comment" : null, suggestion.ReviewComment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Rejection_requires_comment_and_leaves_pending_on_failure(string? comment)
    {
        var suggestion = New();
        var error = Assert.Throws<DomainException>(() => suggestion.Reject(Guid.NewGuid(), Created, comment));
        Assert.Equal(CatalogErrors.SuggestionRejectionCommentRequired, error.Error);
        Assert.Equal(BookSuggestionStatus.Pending, suggestion.Status);
        Assert.Null(suggestion.ReviewedAtUtc);
        Assert.Null(suggestion.ReviewedByUserId);
    }
}
