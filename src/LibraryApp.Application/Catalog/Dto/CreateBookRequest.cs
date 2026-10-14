namespace LibraryApp.Application.Catalog.Dto;

public sealed record CreateBookRequest(
    string Title,
    string Isbn,
    int PublicationYear,
    string Language,
    string? Description,
    Guid CategoryId,
    Guid? PublisherId,
    IReadOnlyList<Guid> AuthorIds);
