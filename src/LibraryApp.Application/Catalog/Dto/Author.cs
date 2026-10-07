namespace LibraryApp.Application.Catalog.Dto;

public sealed record AuthorDto(Guid Id, string FullName);
public sealed record AuthorRequest(string? FullName);
public sealed record BookAuthorsRequest(IReadOnlyList<Guid>? AuthorIds);
