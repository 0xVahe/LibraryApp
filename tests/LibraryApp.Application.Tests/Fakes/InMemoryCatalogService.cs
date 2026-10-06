using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Domain.Catalog;   // BookCopyStatus
using LibraryApp.Domain.Common;    // NotFoundException, ConflictException (check where they really are)

namespace LibraryApp.Application.Tests.Fakes;

/// <summary>
/// In-memory stand-in for Group 1's Catalog. It keeps books and copies in dictionaries
/// and follows the rules Group 1 answered. Used until their real service is merged, and in our unit tests.
/// </summary>
public sealed class InMemoryCatalogService : ICatalogService
{
    private readonly Dictionary<Guid, BookDto> _books = new();
    private readonly Dictionary<Guid, BookCopyDto> _copies = new();

    /// <summary>Test switch: when set, the next SetCopyStatusAsync throws it once.
    /// Used to test that IssueAsync rolls back when the Catalog call fails.</summary>
    public Exception? ThrowOnNextSetCopyStatus { get; set; }

    // ---------- helpers for tests (not part of ICatalogService) ----------

    public BookDto AddBook(string title, string isbn = "9780134685991", bool isDeleted = false)
    {
        var book = new BookDto(Guid.NewGuid(), title, isbn, isDeleted);
        _books[book.Id] = book;
        return book;
    }

    public BookCopyDto AddCopy(Guid bookId, string barcode, BookCopyStatus status = BookCopyStatus.Available)
    {
        var copy = new BookCopyDto(Guid.NewGuid(), bookId, barcode, status);
        _copies[copy.Id] = copy;
        return copy;
    }

    /// <summary>Reads the current status of a copy, so a test can check it after a rollback.</summary>
    public BookCopyStatus StatusOf(Guid copyId) => _copies[copyId].Status;

    // ---------- ICatalogService ----------

    // INT-01: only looks, never changes a status.
    public Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct = default) =>
        Task.FromResult(_copies.Values
            .Where(c => c.BookId == bookId && c.Status == BookCopyStatus.Available)
            .OrderBy(c => c.Barcode)
            .FirstOrDefault());

    // INT-02
    public Task SetCopyStatusAsync(Guid copyId, BookCopyStatus status, CancellationToken ct = default)
    {
        if (ThrowOnNextSetCopyStatus is not null)
        {
            var failure = ThrowOnNextSetCopyStatus;
            ThrowOnNextSetCopyStatus = null;          // only once
            throw failure;
        }

        if (!_copies.TryGetValue(copyId, out var copy))
        {
            throw new DomainException(
                Error.NotFound(
                    "catalog.copy_not_found",
                    $"Copy {copyId} was not found."));
        }

        if (!IsChangeAllowed(copy.Status, status))
        {
            throw new DomainException(
                Error.Conflict(
                    "catalog.copy_status_conflict",
                    $"Copy cannot change from {copy.Status} to {status}."));
        }
        
        // A record is immutable: "with" makes a copy of it with one property changed.
        _copies[copyId] = copy with { Status = status };
        return Task.CompletedTask;
    }

    // CAT-07: deleted books are returned too (IsDeleted = true), null only if the book does not exist.
    public Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken ct = default) =>
        Task.FromResult(_books.GetValueOrDefault(bookId));

    // Agreed with Group 1: case and spaces are ignored.
    public Task<BookCopyDto?> GetCopyByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        var wanted = barcode.Trim();
        return Task.FromResult(_copies.Values.FirstOrDefault(c =>
            string.Equals(c.Barcode.Trim(), wanted, StringComparison.OrdinalIgnoreCase)));
    }

    // PROPOSED methods: simple versions, so code that will need them can already be written.
    public Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BookCopyDto>>(
            _copies.Values.Where(c => copyIds.Contains(c.Id)).ToList());

    public Task<BookCopyDto?> FindCopyAsync(Guid bookId, BookCopyStatus status, CancellationToken ct = default) =>
        Task.FromResult(_copies.Values.FirstOrDefault(c => c.BookId == bookId && c.Status == status));

    public Task<IReadOnlyList<Guid>> GetCopyIdsAsync(Guid bookId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(
            _copies.Values.Where(c => c.BookId == bookId).Select(c => c.Id).ToList());

    // Rules from Group 1's answer:
    //  - OnLoan is allowed only from Available or Reserved (OnLoan -> OnLoan is a 409).
    //  - Lost and Withdrawn are final.
    // Everything else is allowed for now, because Group 1 has not confirmed the full matrix yet (BQ-G1-06).
    private static bool IsChangeAllowed(BookCopyStatus from, BookCopyStatus to)
    {
        if (from == to) return false;
        if (from is BookCopyStatus.Lost or BookCopyStatus.Withdrawn) return false;
        if (to == BookCopyStatus.OnLoan) return from is BookCopyStatus.Available or BookCopyStatus.Reserved;
        return true;
    }
}