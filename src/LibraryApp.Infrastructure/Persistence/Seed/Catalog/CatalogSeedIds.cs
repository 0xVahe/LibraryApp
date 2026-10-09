namespace LibraryApp.Infrastructure.Persistence.Seed.Catalog;

/// <summary>Stable identifiers for catalog reference and book seed data.</summary>
public static class CatalogSeedIds
{
    public static Guid Category(int number) =>
        new($"10000000-0000-0000-0001-{number:D12}");

    public static Guid Book(int number) =>
        new($"b0000000-0000-0000-0000-{number:D12}");
}
