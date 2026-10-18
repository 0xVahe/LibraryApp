using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>
/// What Catalog offers other modules (CAT-07, INT-01, INT-02). Reads never track anything.
/// <see cref="SetCopyStatusAsync"/> only changes the tracked copy: it opens no transaction and does not save, so the
/// caller's <c>IUnitOfWork</c> saves it together with the loan (BR-BOR-4), and the copy's RowVersion turns two parallel
/// changes of the same copy into a 409.
/// </summary>
public sealed class CatalogService(
    ICatalogCopyQuery copyQuery,
    IBookCopyRepository copies,
    IBookDetailsQuery books,
    ICatalogCoverUrlBuilder coverUrls) : ICatalogService
{
    public Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct = default) =>
        copyQuery.GetAvailableCopyAsync(bookId, ct);

    public async Task SetCopyStatusAsync(Guid copyId, BookCopyStatus status, CancellationToken ct = default)
    {
        BookCopy copy = await copies.GetByIdAsync(copyId, ct)
            ?? throw new DomainException(CatalogErrors.CopyNotFound);

        // The transition table of Catalog applies: an illegal change throws catalog.copy_status_change_not_allowed.
        copy.ChangeStatus(status);
    }

    public async Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken ct = default)
    {
        BookDto? book = await books.GetBookAsync(bookId, ct);
        return book is null
            ? book
            : book with { CoverImageUrl = coverUrls.Build(book.CoverImageUrl) };
    }

    public Task<BookCopyDto?> GetCopyByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return Task.FromResult<BookCopyDto?>(null);

        // Barcodes are stored trimmed and upper case (G1-06), so a scanned " bk-0001-01" still matches.
        return copyQuery.GetByBarcodeAsync(barcode.Trim().ToUpperInvariant(), ct);
    }

    public async Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(copyIds);

        if (copyIds.Count == 0)
            return [];

        return await copyQuery.GetByIdsAsync(copyIds.Distinct().ToArray(), ct);
    }

    public Task<IReadOnlyList<Guid>> GetCopyIdsAsync(Guid bookId, CancellationToken ct = default) =>
        copyQuery.GetIdsByBookAsync(bookId, ct);
}
