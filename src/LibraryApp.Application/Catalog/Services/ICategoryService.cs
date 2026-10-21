using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Categories in two levels at most (CAT-03). A category in use is never deleted.</summary>
public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct = default);
    Task<CategoryDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CategoryDto> CreateAsync(CategoryRequest request, CancellationToken ct = default);
    Task UpdateAsync(Guid id, CategoryRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
