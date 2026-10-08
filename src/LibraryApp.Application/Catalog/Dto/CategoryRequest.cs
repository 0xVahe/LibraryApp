namespace LibraryApp.Application.Catalog.Dto;

/// <summary>Creates or changes a category (CAT-03).</summary>
/// <param name="Name">Required, up to 100 characters.</param>
/// <param name="ParentCategoryId">Null makes a top-level category; otherwise the id of a top-level category (two levels at most).</param>
public sealed record CategoryRequest(string? Name, Guid? ParentCategoryId);
