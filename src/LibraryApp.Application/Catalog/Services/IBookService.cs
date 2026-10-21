using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Creates, updates and soft-deletes books (CAT-01, BR-CAT-1, BR-CAT-3, BR-CAT-5).</summary>
public interface IBookService
{
    Task<Guid> CreateAsync(
        CreateBookRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Creates several books in one transaction (CAT-09): all of them, or none when one breaks a rule.
    /// The IDs come back in the order of the requests.
    /// </summary>
    Task<IReadOnlyList<Guid>> CreateBatchAsync(
        IReadOnlyList<CreateBookRequest> requests,
        CancellationToken ct = default);

    Task UpdateAsync(
        Guid id,
        UpdateBookRequest request,
        CancellationToken ct = default);

    Task DeleteAsync(
        Guid id,
        CancellationToken ct = default);
}
