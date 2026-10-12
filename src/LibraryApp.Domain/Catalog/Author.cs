using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Catalog;

public sealed class Author : Entity
{
    public const int FullNameMaxLength = 200;

    private Author() { }                                         // for EF Core only

    public Author(Guid id, string fullName) : base(id)
    {
        Rename(fullName);
    }

    public string FullName { get; private set; } = null!;

    public void Rename(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException(CatalogErrors.AuthorNameRequired);

        FullName = fullName.Trim();
    }
}
