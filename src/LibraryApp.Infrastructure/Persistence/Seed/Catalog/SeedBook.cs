namespace LibraryApp.Infrastructure.Persistence.Seed.Catalog;

public sealed record SeedBook(
    int Number,
    string Title,
    IReadOnlyList<string> Authors,
    string? Publisher,
    int PublicationYear,
    string Language,
    string Isbn,
    int CategoryNumber);
