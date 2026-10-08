using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Catalog;

public class CategoryServiceTests
{
    private readonly FakeCategories _repository = new();
    private readonly TestTransaction _transaction = new();
    private CategoryService Service => new(_repository, _transaction, _transaction, _transaction);
    private Task<CategoryDto> Create(string name, Guid? parent = null) => Service.CreateAsync(new CategoryRequest(name, parent));

    [Fact]
    public async Task Child_can_be_renamed_moved_to_another_root_and_promoted_to_root()
    {
        var first = await Create("First");
        var second = await Create("Second");
        var child = await Create("Child", first.Id);
        await Service.UpdateAsync(child.Id, new CategoryRequest("Renamed", second.Id));
        Assert.Equal(new CategoryDto(child.Id, "Renamed", second.Id), await Service.GetAsync(child.Id));
        Assert.Empty(_repository.Categories.Single(x => x.Id == first.Id).Children);
        await Service.UpdateAsync(child.Id, new CategoryRequest("Renamed", null));
        Assert.Null((await Service.GetAsync(child.Id)).ParentCategoryId);
        Assert.Empty(_repository.Categories.Single(x => x.Id == second.Id).Children);
        Assert.Equal(5, _transaction.Locks.Count);
    }

    [Fact]
    public async Task Third_level_cannot_be_created()
    {
        var root = await Create("Root");
        var child = await Create("Child", root.Id);
        var error = await Assert.ThrowsAsync<DomainException>(() => Create("Third", child.Id));
        Assert.Equal(CatalogErrors.CategoryParentIsNotTopLevel, error.Error);
        Assert.Equal(2, _repository.Categories.Count);
    }

    [Fact]
    public async Task Root_with_children_cannot_be_moved_under_another_root()
    {
        var root = await Create("Root");
        var other = await Create("Other");
        var child = await Create("Child", root.Id);
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            Service.UpdateAsync(root.Id, new CategoryRequest("Root", other.Id)));
        Assert.Equal(CatalogErrors.CategoryHasSubcategories, error.Error);
        Assert.Null((await Service.GetAsync(root.Id)).ParentCategoryId);
        Assert.Equal(root.Id, (await Service.GetAsync(child.Id)).ParentCategoryId);
    }

    [Fact]
    public async Task Self_parenting_is_rejected()
    {
        var root = await Create("Root");
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            Service.UpdateAsync(root.Id, new CategoryRequest("Root", root.Id)));
        Assert.Equal(CatalogErrors.CategoryParentIsItself, error.Error);
    }

    [Fact]
    public async Task Moving_root_under_its_child_is_rejected_without_changing_relationships()
    {
        var root = await Create("Root");
        var child = await Create("Child", root.Id);
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            Service.UpdateAsync(root.Id, new CategoryRequest("Root", child.Id)));
        Assert.Equal(CatalogErrors.CategoryParentIsNotTopLevel, error.Error);
        Assert.Null((await Service.GetAsync(root.Id)).ParentCategoryId);
        Assert.Equal(root.Id, (await Service.GetAsync(child.Id)).ParentCategoryId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Referenced_category_cannot_be_deleted(bool hasChild)
    {
        var root = await Create("Root");
        if (hasChild) await Create("Child", root.Id);
        else _repository.ReferencedIds.Add(root.Id);
        var error = await Assert.ThrowsAsync<DomainException>(() => Service.DeleteAsync(root.Id));
        Assert.Equal(CatalogErrors.CategoryInUse, error.Error);
        Assert.Contains(_repository.Categories, x => x.Id == root.Id);
    }

    [Fact]
    public async Task Unreferenced_category_can_be_deleted()
    {
        var root = await Create("Root");
        await Service.DeleteAsync(root.Id);
        Assert.Empty(await Service.ListAsync());
    }

    [Fact]
    public async Task Missing_parent_is_rejected()
    {
        var error = await Assert.ThrowsAsync<DomainException>(() => Create("Child", Guid.NewGuid()));
        Assert.Equal(CatalogErrors.CategoryNotFound, error.Error);
        Assert.Empty(_repository.Categories);
    }
}
