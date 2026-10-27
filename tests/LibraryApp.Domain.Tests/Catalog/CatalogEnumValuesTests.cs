using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Tests.Catalog;

/// <summary>
/// The numbers are stored in the database: if one changes, existing rows change meaning.
/// These tests fail when somebody renumbers an enum.
/// </summary>
public class CatalogEnumValuesTests
{
    [Theory]
    [InlineData(BookCopyStatus.Available, 1)]
    [InlineData(BookCopyStatus.OnLoan, 2)]
    [InlineData(BookCopyStatus.Reserved, 3)]
    [InlineData(BookCopyStatus.UnderRepair, 4)]
    [InlineData(BookCopyStatus.Lost, 5)]
    [InlineData(BookCopyStatus.Withdrawn, 6)]
    public void BookCopyStatus_HasTheAgreedNumber(BookCopyStatus status, int number)
    {
        Assert.Equal(number, (int)status);
    }

    [Theory]
    [InlineData(BookCopyCondition.New, 1)]
    [InlineData(BookCopyCondition.Good, 2)]
    [InlineData(BookCopyCondition.Fair, 3)]
    [InlineData(BookCopyCondition.Poor, 4)]
    public void BookCopyCondition_HasTheAgreedNumber(BookCopyCondition condition, int number)
    {
        Assert.Equal(number, (int)condition);
    }

    [Fact]
    public void EveryCatalogErrorCode_StartsWithTheModulePrefix()
    {
        var errors = new[]
        {
            CatalogErrors.BookTitleRequired,
            CatalogErrors.BookIsbnRequired,
            CatalogErrors.BookLanguageRequired,
            CatalogErrors.BookCategoryRequired,
            CatalogErrors.BookAlreadyDeleted,
            CatalogErrors.AuthorNameRequired,
            CatalogErrors.PublisherNameRequired,
            CatalogErrors.CategoryNameRequired,
            CatalogErrors.CategoryParentIsItself,
            CatalogErrors.CategoryParentIsNotTopLevel,
            CatalogErrors.CategoryHasSubcategories,
            CatalogErrors.CopyBarcodeRequired,
            CatalogErrors.CopyConditionUnknown,
            CatalogErrors.CopyStatusChangeNotAllowed(BookCopyStatus.Lost, BookCopyStatus.Available),
        };

        foreach (Error error in errors)
        {
            Assert.StartsWith("catalog.", error.Code);
        }
    }
}
