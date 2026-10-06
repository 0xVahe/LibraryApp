using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Tests.Catalog;

public class BookCopyTests
{
    private static BookCopy NewCopy(string barcode = "BK-0001-01")
    {
        return new BookCopy(Guid.NewGuid(), Guid.NewGuid(), barcode, BookCopyCondition.Good, null);
    }

    // ---------- creating a copy ----------

    [Fact]
    public void NewCopy_IsAvailable_WithTrimmedUpperCaseBarcodeAndShelf()
    {
        var copy = new BookCopy(Guid.NewGuid(), Guid.NewGuid(), "  bk-0001-01 ", BookCopyCondition.New, "  Shelf B-3 ");

        Assert.Equal(BookCopyStatus.Available, copy.Status);
        Assert.Equal("BK-0001-01", copy.Barcode);
        Assert.Equal("Shelf B-3", copy.ShelfLocation);
    }

    [Fact]
    public void NewCopy_BlankShelfLocation_IsStoredAsNull()
    {
        var copy = new BookCopy(Guid.NewGuid(), Guid.NewGuid(), "BK-1", BookCopyCondition.Good, "   ");

        Assert.Null(copy.ShelfLocation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NewCopy_BlankBarcode_Throws(string barcode)
    {
        var exception = Assert.Throws<DomainException>(() => NewCopy(barcode));

        Assert.Equal(CatalogErrors.CopyBarcodeRequired, exception.Error);
    }

    [Fact]
    public void NewCopy_UnknownCondition_Throws()
    {
        var exception = Assert.Throws<DomainException>(
            () => new BookCopy(Guid.NewGuid(), Guid.NewGuid(), "BK-1", (BookCopyCondition)99, null));

        Assert.Equal(CatalogErrors.CopyConditionUnknown, exception.Error);
    }

    [Fact]
    public void NewCopy_ConditionZeroMeansNotSet_Throws()
    {
        var exception = Assert.Throws<DomainException>(
            () => new BookCopy(Guid.NewGuid(), Guid.NewGuid(), "BK-1", default, null));

        Assert.Equal(CatalogErrors.CopyConditionUnknown, exception.Error);
    }

    // ---------- status transitions ----------

    [Theory]
    [InlineData(BookCopyStatus.Available, BookCopyStatus.OnLoan)]
    [InlineData(BookCopyStatus.OnLoan, BookCopyStatus.Available)]
    [InlineData(BookCopyStatus.OnLoan, BookCopyStatus.Reserved)]
    [InlineData(BookCopyStatus.OnLoan, BookCopyStatus.UnderRepair)]
    [InlineData(BookCopyStatus.OnLoan, BookCopyStatus.Lost)]
    [InlineData(BookCopyStatus.Reserved, BookCopyStatus.OnLoan)]
    [InlineData(BookCopyStatus.Reserved, BookCopyStatus.Available)]
    [InlineData(BookCopyStatus.UnderRepair, BookCopyStatus.Available)]
    public void Transition_Allowed(BookCopyStatus from, BookCopyStatus to)
    {
        Assert.True(BookCopyStatusTransitions.IsAllowed(from, to));
    }

    [Theory]
    [InlineData(BookCopyStatus.OnLoan, BookCopyStatus.OnLoan)]        // second loan of the same copy
    [InlineData(BookCopyStatus.OnLoan, BookCopyStatus.Withdrawn)]     // cannot withdraw a copy that is out
    [InlineData(BookCopyStatus.UnderRepair, BookCopyStatus.OnLoan)]   // damaged copy is not lent
    [InlineData(BookCopyStatus.Lost, BookCopyStatus.Available)]       // Lost is final
    [InlineData(BookCopyStatus.Withdrawn, BookCopyStatus.Available)]  // Withdrawn is final
    public void Transition_NotAllowed(BookCopyStatus from, BookCopyStatus to)
    {
        Assert.False(BookCopyStatusTransitions.IsAllowed(from, to));
    }

    [Fact]
    public void ChangeStatus_Allowed_ChangesTheStatus()
    {
        var copy = NewCopy();

        copy.ChangeStatus(BookCopyStatus.OnLoan);

        Assert.Equal(BookCopyStatus.OnLoan, copy.Status);
    }

    [Fact]
    public void ChangeStatus_NotAllowed_ThrowsConflict_AndKeepsOldStatus()
    {
        var copy = NewCopy();
        copy.ChangeStatus(BookCopyStatus.Withdrawn);

        var exception = Assert.Throws<DomainException>(() => copy.ChangeStatus(BookCopyStatus.Available));

        Assert.Equal(ErrorKind.Conflict, exception.Error.Kind);
        Assert.Equal("catalog.copy_status_change_not_allowed", exception.Error.Code);
        Assert.Equal(BookCopyStatus.Withdrawn, copy.Status);
    }

    // ---------- editing ----------

    [Fact]
    public void UpdateDetails_ChangesConditionAndShelf_ButNotBarcodeOrStatus()
    {
        var copy = NewCopy("BK-7");
        copy.ChangeStatus(BookCopyStatus.OnLoan);

        copy.UpdateDetails(BookCopyCondition.Poor, " C-7 ");

        Assert.Equal(BookCopyCondition.Poor, copy.Condition);
        Assert.Equal("C-7", copy.ShelfLocation);
        Assert.Equal("BK-7", copy.Barcode);
        Assert.Equal(BookCopyStatus.OnLoan, copy.Status);
    }

    [Fact]
    public void UpdateDetails_UnknownCondition_Throws()
    {
        var copy = NewCopy();

        var exception = Assert.Throws<DomainException>(() => copy.UpdateDetails((BookCopyCondition)99, null));

        Assert.Equal(CatalogErrors.CopyConditionUnknown, exception.Error);
    }
}
