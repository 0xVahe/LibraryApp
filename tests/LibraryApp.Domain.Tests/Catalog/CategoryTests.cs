using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Tests.Catalog;

public class CategoryTests
{
    private static Category NewCategory(string name = "Programming")
    {
        return new Category(Guid.NewGuid(), name);
    }

    [Fact]
    public void NewCategory_IsTopLevel_AndNameIsTrimmed()
    {
        var category = NewCategory("  Programming ");

        Assert.Equal("Programming", category.Name);
        Assert.Null(category.ParentCategoryId);
    }

    [Fact]
    public void NewCategory_BlankName_Throws()
    {
        var exception = Assert.Throws<DomainException>(() => NewCategory(" "));

        Assert.Equal(CatalogErrors.CategoryNameRequired, exception.Error);
    }

    [Fact]
    public void SetParent_TopLevelParent_Works()
    {
        var parent = NewCategory("Technology");
        var child = NewCategory("C#");

        child.SetParent(parent);

        Assert.Equal(parent.Id, child.ParentCategoryId);
        Assert.Single(parent.Children);
    }

    [Fact]
    public void SetParent_ParentThatAlreadyHasAParent_ThrowsBusinessRule()
    {
        var root = NewCategory("Technology");
        var middle = NewCategory("Programming");
        middle.SetParent(root);
        var third = NewCategory("C#");

        var exception = Assert.Throws<DomainException>(() => third.SetParent(middle));

        Assert.Equal(CatalogErrors.CategoryParentIsNotTopLevel, exception.Error);
        Assert.Null(third.ParentCategoryId);
    }

    [Fact]
    public void SetParent_CategoryThatHasSubcategories_ThrowsBusinessRule()
    {
        var root = NewCategory("Technology");
        var other = NewCategory("Science");
        var withChild = NewCategory("Programming");
        var child = NewCategory("C#");
        child.SetParent(withChild);

        var exception = Assert.Throws<DomainException>(() => withChild.SetParent(root));

        Assert.Equal(CatalogErrors.CategoryHasSubcategories, exception.Error);
        Assert.Null(withChild.ParentCategoryId);
        Assert.Empty(other.Children);
    }

    [Fact]
    public void SetParent_Itself_ThrowsBusinessRule()
    {
        var category = NewCategory();

        var exception = Assert.Throws<DomainException>(() => category.SetParent(category));

        Assert.Equal(CatalogErrors.CategoryParentIsItself, exception.Error);
    }

    [Fact]
    public void SetParent_Null_MakesTheCategoryTopLevelAgain()
    {
        var parent = NewCategory("Technology");
        var child = NewCategory("C#");
        child.SetParent(parent);

        child.SetParent(null);

        Assert.Null(child.ParentCategoryId);
        Assert.Empty(parent.Children);
    }

    [Fact]
    public void SetParent_MovingToAnotherParent_UpdatesBothSides()
    {
        var first = NewCategory("Technology");
        var second = NewCategory("Science");
        var child = NewCategory("C#");
        child.SetParent(first);

        child.SetParent(second);

        Assert.Equal(second.Id, child.ParentCategoryId);
        Assert.Empty(first.Children);
        Assert.Single(second.Children);
    }
}
