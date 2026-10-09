namespace LibraryApp.Infrastructure.Persistence.Seed.Catalog;

/// <summary>Stable identifiers for catalog reference and book seed data.</summary>
public static class CatalogSeedIds
{
    public static Guid Category(int number) =>
        new($"10000000-0000-0000-0001-{number:000000000000}");

    public static Guid Book(int number) =>
        new($"30000000-0000-0000-0001-{number:000000000000}");

    public static Guid Author(int number) =>
        new($"30000000-0000-0000-0002-{number:000000000000}");

    public static Guid Publisher(int number) =>
        new($"30000000-0000-0000-0003-{number:000000000000}");
}
