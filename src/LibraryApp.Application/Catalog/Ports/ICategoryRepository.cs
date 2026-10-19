using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Ports;

/// <summary>Categories (CAT-03); lists are read without tracking.</summary>
public interface ICategoryRepository
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct);
    /// <summary>Loads the category, its parent, and its children for hierarchy checks.</summary>
    Task<Category?> FindAsync(Guid id, CancellationToken ct);
    Task<bool> HasBooksAsync(Guid id, CancellationToken ct);
    void Add(Category category);
    void Remove(Category category);
}
