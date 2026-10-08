namespace LibraryApp.Application.Catalog.Dto;

/// <summary>A category (CAT-03). ParentCategoryId is null for a top-level category.</summary>
public sealed record CategoryDto(Guid Id, string Name, Guid? ParentCategoryId);
