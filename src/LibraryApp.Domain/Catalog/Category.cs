using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Catalog;

/// <summary>
/// A book category with at most two levels (CAT-03, G1-04). The rule is checked here, in <see cref="SetParent"/>:
/// the service loads the parent and the subcategories of this category before calling it.
/// </summary>
public sealed class Category : Entity
{
    public const int NameMaxLength = 100;

    private readonly List<Category> _children = new();

    private Category() { }                                       // for EF Core only

    public Category(Guid id, string name) : base(id)
    {
        Rename(name);
    }

    public string Name { get; private set; } = null!;

    public Guid? ParentCategoryId { get; private set; }

    public Category? Parent { get; private set; }

    public IReadOnlyCollection<Category> Children => _children;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(CatalogErrors.CategoryNameRequired);

        Name = name.Trim();
    }

    /// <summary>Puts the category under a top-level category, or makes it top-level when parent is null.</summary>
    public void SetParent(Category? parent)
    {
        if (parent is null)
        {
            Parent?._children.Remove(this);
            Parent = null;
            ParentCategoryId = null;
            return;
        }

        if (parent.Id == Id)
            throw new DomainException(CatalogErrors.CategoryParentIsItself);

        // The parent must be a top-level category, otherwise there would be three levels.
        if (parent.ParentCategoryId is not null)
            throw new DomainException(CatalogErrors.CategoryParentIsNotTopLevel);

        // This category must not have subcategories of its own, otherwise there would be three levels.
        if (_children.Count > 0)
            throw new DomainException(CatalogErrors.CategoryHasSubcategories);

        Parent?._children.Remove(this);
        Parent = parent;
        ParentCategoryId = parent.Id;
        parent._children.Add(this);                              // keeps both sides of the relationship in step
    }
}
