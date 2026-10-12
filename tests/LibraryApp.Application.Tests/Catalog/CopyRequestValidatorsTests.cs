using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Validators;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Tests.Catalog;

public class CopyRequestValidatorsTests
{
    private readonly AddCopyRequestValidator _add = new();
    private readonly UpdateCopyRequestValidator _update = new();
    private readonly ChangeCopyStatusRequestValidator _status = new();

    [Theory]
    [InlineData("BK-0001-01")]
    [InlineData(" bk-0001-01 ")]                         // trimmed and upper-cased by the entity
    [InlineData("12345678901234567890123456789012")]     // 32 characters
    public void A_valid_barcode_is_accepted(string barcode)
    {
        Assert.True(_add.Validate(new AddCopyRequest(barcode, BookCopyCondition.Good, "A-01")).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BK 01")]
    [InlineData("BK_01")]
    [InlineData("123456789012345678901234567890123")]    // 33 characters
    public void An_invalid_barcode_is_refused(string barcode)
    {
        var result = _add.Validate(new AddCopyRequest(barcode, BookCopyCondition.Good, null));

        Assert.False(result.IsValid);
        Assert.All(result.Errors, e => Assert.Equal(nameof(AddCopyRequest.Barcode), e.PropertyName));
    }

    [Fact]
    public void An_unknown_condition_is_refused()
    {
        Assert.False(_add.Validate(new AddCopyRequest("BK-1", (BookCopyCondition)99, null)).IsValid);
        Assert.False(_update.Validate(new UpdateCopyRequest((BookCopyCondition)99, null)).IsValid);
    }

    [Fact]
    public void A_shelf_location_longer_than_50_characters_is_refused()
    {
        string shelf = new('A', BookCopy.ShelfLocationMaxLength + 1);

        Assert.False(_add.Validate(new AddCopyRequest("BK-1", BookCopyCondition.Good, shelf)).IsValid);
        Assert.False(_update.Validate(new UpdateCopyRequest(BookCopyCondition.Good, shelf)).IsValid);
    }

    [Fact]
    public void An_unknown_status_is_refused()
    {
        Assert.True(_status.Validate(new ChangeCopyStatusRequest(BookCopyStatus.Lost)).IsValid);
        Assert.False(_status.Validate(new ChangeCopyStatusRequest((BookCopyStatus)0)).IsValid);
    }
}
