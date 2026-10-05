namespace LibraryApp.Domain.Common;

public abstract class Entity
{
    protected Entity() { }

    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("An entity identifier must not be empty.", nameof(id));

        Id = id;
    }

    public Guid Id { get; private set; }
}
