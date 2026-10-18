using LibraryApp.Api.Areas.Catalog;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>CAT-04, BR-CAT-4: the copies panel offers only the status changes the copy service accepts.</summary>
public class CopyStatusActionsTests
{
    [Theory]
    [InlineData(BookCopyStatus.Available, new[] { BookCopyStatus.UnderRepair, BookCopyStatus.Lost, BookCopyStatus.Withdrawn })]
    [InlineData(BookCopyStatus.UnderRepair, new[] { BookCopyStatus.Available, BookCopyStatus.Lost, BookCopyStatus.Withdrawn })]
    [InlineData(BookCopyStatus.OnLoan, new BookCopyStatus[0])]
    [InlineData(BookCopyStatus.Reserved, new BookCopyStatus[0])]
    [InlineData(BookCopyStatus.Lost, new BookCopyStatus[0])]
    [InlineData(BookCopyStatus.Withdrawn, new BookCopyStatus[0])]
    public void Each_status_offers_exactly_the_allowed_changes(BookCopyStatus current, BookCopyStatus[] expected)
    {
        Assert.Equal(expected, CopyStatusActions.For(current).Select(action => action.Status));
    }

    [Fact]
    public void Every_offered_change_is_allowed_by_the_domain_and_never_sets_a_Borrowing_status()
    {
        foreach (var current in Enum.GetValues<BookCopyStatus>())
        {
            foreach (var action in CopyStatusActions.For(current))
            {
                Assert.True(BookCopyStatusTransitions.IsAllowed(current, action.Status), $"{current} → {action.Status}");
                Assert.DoesNotContain(action.Status, new[] { BookCopyStatus.OnLoan, BookCopyStatus.Reserved });
            }
        }
    }

    [Theory]
    [InlineData(BookCopyStatus.Available, true)]
    [InlineData(BookCopyStatus.UnderRepair, true)]
    [InlineData(BookCopyStatus.OnLoan, true)]
    [InlineData(BookCopyStatus.Reserved, true)]
    [InlineData(BookCopyStatus.Lost, false)]
    [InlineData(BookCopyStatus.Withdrawn, false)]
    public void Shelf_and_condition_are_edited_only_for_copies_still_in_the_collection(BookCopyStatus current, bool editable)
    {
        Assert.Equal(editable, CopyStatusActions.CanEditDetails(current));
    }
}
