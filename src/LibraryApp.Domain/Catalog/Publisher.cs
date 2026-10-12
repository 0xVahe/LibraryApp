using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Catalog;

public sealed class Publisher : Entity
{
    public const int NameMaxLength = 200;

    private Publisher() { }                                      // for EF Core only

    public Publisher(Guid id, string name) : base(id)
    {
        Rename(name);
    }

    public string Name { get; private set; } = null!;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(CatalogErrors.PublisherNameRequired);

        Name = name.Trim();
    }
}
