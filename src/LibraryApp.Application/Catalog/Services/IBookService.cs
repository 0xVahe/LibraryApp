using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Services;

public interface IBookService
{
    Task<Guid> CreateAsync(
        CreateBookRequest request,
        CancellationToken ct = default);

    Task UpdateAsync(
        Guid id,
        UpdateBookRequest request,
        CancellationToken ct = default);

    Task DeleteAsync(
        Guid id,
        CancellationToken ct = default);
}
