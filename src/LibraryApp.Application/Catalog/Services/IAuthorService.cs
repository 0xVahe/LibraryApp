using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Services;

public interface IAuthorService
{
    Task<IReadOnlyList<AuthorDto>> ListAsync(CancellationToken ct = default);
    Task<AuthorDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<AuthorDto> CreateAsync(AuthorRequest request, CancellationToken ct = default);
    Task UpdateAsync(Guid id, AuthorRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Replaces an existing book's authors atomically; at least one author is required (BR-CAT-5).</summary>
    Task SetBookAuthorsAsync(Guid bookId, BookAuthorsRequest request, CancellationToken ct = default);
}
