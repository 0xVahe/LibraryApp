namespace LibraryApp.Application.Catalog.Dto;

public sealed record CategoryDto(Guid Id, string Name, Guid? ParentCategoryId);
public sealed record CategoryRequest(string? Name, Guid? ParentCategoryId);
