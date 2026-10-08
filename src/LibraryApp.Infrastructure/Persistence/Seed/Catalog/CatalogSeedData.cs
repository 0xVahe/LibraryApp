namespace LibraryApp.Infrastructure.Persistence.Seed.Catalog;

/// <summary>Stable reference IDs for the eight required categories (S1-04, G1-10).</summary>
public static class CatalogSeedData
{
    public static readonly IReadOnlyList<(Guid Id, string Name)> Categories =
    [
        (new("10000000-0000-0000-0001-000000000001"), "Banking & Finance"),
        (new("10000000-0000-0000-0001-000000000002"), "Risk, Compliance & AML"),
        (new("10000000-0000-0000-0001-000000000003"), "IT & Software Engineering"),
        (new("10000000-0000-0000-0001-000000000004"), "Data & Analytics"),
        (new("10000000-0000-0000-0001-000000000005"), "Leadership & Management"),
        (new("10000000-0000-0000-0001-000000000006"), "Soft Skills & Communication"),
        (new("10000000-0000-0000-0001-000000000007"), "Customer Experience"),
        (new("10000000-0000-0000-0001-000000000008"), "General Reading"),
    ];
}
