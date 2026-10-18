using LibraryApp.Api.Areas.Catalog;
using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>The category helper of the Catalog pages: plain objects, no host.</summary>
public class CategoryTreeTests
{
    private static readonly CategoryDto Finance = new(Guid.NewGuid(), "Banking & Finance", null);
    private static readonly CategoryDto Retail = new(Guid.NewGuid(), "Retail", Finance.Id);
    private static readonly CategoryDto Corporate = new(Guid.NewGuid(), "Corporate", Finance.Id);
    private static readonly CategoryDto Data = new(Guid.NewGuid(), "Data & Analytics", null);

    private static CategoryTree Tree() => new([Retail, Data, Corporate, Finance]);

    [Fact]
    public void Top_level_categories_are_the_chips_by_name() =>
        Assert.Equal(new[] { Finance.Id, Data.Id }, Tree().TopLevel.Select(x => x.Id));

    [Fact]
    public void A_subcategory_path_has_its_parent_and_a_top_level_one_only_its_name()
    {
        var tree = Tree();
        Assert.Equal("Banking & Finance / Retail", tree.PathOf(Retail.Id));
        Assert.Equal("Data & Analytics", tree.PathOf(Data.Id));
        Assert.Null(tree.PathOf(Guid.NewGuid()));
    }

    [Fact]
    public void The_select_lists_each_parent_followed_by_its_subcategories()
    {
        Assert.Equal(
            new[] { "Banking & Finance", "Banking & Finance / Corporate", "Banking & Finance / Retail", "Data & Analytics" },
            Tree().Options.Select(x => x.Path));
    }

    [Fact]
    public void Parent_and_children_are_found_by_ID()
    {
        var tree = Tree();
        Assert.Equal(Finance, tree.ParentOf(Retail.Id));
        Assert.Null(tree.ParentOf(Finance.Id));
        Assert.Equal(new[] { Corporate.Id, Retail.Id }, tree.ChildrenOf(Finance.Id).Select(x => x.Id));
        Assert.Empty(tree.ChildrenOf(Retail.Id));
    }

    [Fact]
    public void A_category_whose_parent_is_missing_is_shown_as_top_level()
    {
        var orphan = new CategoryDto(Guid.NewGuid(), "Orphan", Guid.NewGuid());
        var tree = new CategoryTree([Finance, orphan]);
        Assert.Contains(tree.TopLevel, x => x.Id == orphan.Id);
        Assert.Equal("Orphan", tree.PathOf(orphan.Id));
        Assert.Contains(tree.Options, x => x.Id == orphan.Id && x.Path == "Orphan");
    }
}
