using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Api.Areas.Catalog;

/// <summary>One choice of a category select: the category ID and its path, for example "Banking &amp; Finance / Retail".</summary>
public sealed record CategoryOption(Guid Id, string Path);

/// <summary>
/// The categories of <c>ICategoryService.ListAsync</c> as the Catalog pages show them (CAT-03: two levels at most):
/// the top-level categories for the catalog chips, the "Parent / Child" path for the book page, and the choices of the
/// book form. Built once per request from the list; it never calls a service itself.
/// </summary>
public sealed class CategoryTree
{
    private const string PathSeparator = " / ";

    private readonly Dictionary<Guid, CategoryDto> _byId;

    public CategoryTree(IReadOnlyList<CategoryDto> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        _byId = categories.ToDictionary(category => category.Id);

        // A category whose parent is missing from the list is shown as a top-level one, so it never disappears.
        TopLevel = categories
            .Where(category => ParentOf(category.Id) is null)
            .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var options = new List<CategoryOption>();
        foreach (var parent in TopLevel)
        {
            options.Add(new CategoryOption(parent.Id, parent.Name));
            options.AddRange(ChildrenOf(parent.Id).Select(child => new CategoryOption(child.Id, parent.Name + PathSeparator + child.Name)));
        }
        Options = options;
    }

    /// <summary>Top-level categories, by name: the chips of the Catalog page.</summary>
    public IReadOnlyList<CategoryDto> TopLevel { get; }

    /// <summary>Every category for a select: each top-level one followed by its subcategories, all by name.</summary>
    public IReadOnlyList<CategoryOption> Options { get; }

    /// <summary>The subcategories of a category, by name; empty for a subcategory or an unknown ID.</summary>
    public IReadOnlyList<CategoryDto> ChildrenOf(Guid categoryId) =>
        _byId.Values
            .Where(category => category.ParentCategoryId == categoryId)
            .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The parent of a subcategory; null for a top-level category, an unknown ID, or a parent missing from the list.</summary>
    public CategoryDto? ParentOf(Guid categoryId) =>
        _byId.TryGetValue(categoryId, out var category)
        && category.ParentCategoryId is { } parentId
        && _byId.TryGetValue(parentId, out var parent)
            ? parent
            : null;

    /// <summary>"Parent / Child" for a subcategory, the name alone for a top-level category, null for an unknown ID.</summary>
    public string? PathOf(Guid categoryId)
    {
        if (!_byId.TryGetValue(categoryId, out var category))
            return null;

        return ParentOf(categoryId) is { } parent ? parent.Name + PathSeparator + category.Name : category.Name;
    }
}
