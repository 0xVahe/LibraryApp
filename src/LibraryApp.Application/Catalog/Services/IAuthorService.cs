using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Authors and the authors of a book (CAT-02). An author linked to a book is never deleted.</summary>
public interface IAuthorService
{
    Task<IReadOnlyList<AuthorDto>> ListAsync(CancellationToken ct = default);
    Task<AuthorDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<AuthorDto> CreateAsync(AuthorRequest request, CancellationToken ct = default);
    Task UpdateAsync(Guid id, AuthorRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns the author IDs of an existing, non-deleted book (CAT-02).</summary>
    Task<IReadOnlyList<Guid>> GetBookAuthorIdsAsync(Guid bookId, CancellationToken ct = default);
    /// <summary>Replaces an existing book's authors atomically; at least one author is required (BR-CAT-5).</summary>
    Task SetBookAuthorsAsync(Guid bookId, BookAuthorsRequest request, CancellationToken ct = default);
}
