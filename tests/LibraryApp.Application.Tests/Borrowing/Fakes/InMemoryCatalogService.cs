using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Borrowing.Fakes;

public sealed class InMemoryCatalogService : ICatalogService
{
    private Dictionary<Guid, BookCopyDto> _copies = new();

    // Null until a test seeds a book: GetBookAsync then fails loudly instead of answering for an unexpected caller.
    private Dictionary<Guid, BookDto>? _books;

    public bool ThrowOnNextSetCopyStatus { get; set; }

    public int LookupCalls { get; private set; }
    public int SetStatusCalls { get; private set; }

    public void Seed(BookCopyDto copy) => _copies.Add(copy.Id, copy);

    /// <summary>A book for <see cref="GetBookAsync"/>; a book that is not seeded is unknown (null).</summary>
    public void SeedBook(Guid bookId, bool isDeleted)
    {
        _books ??= new();
        _books[bookId] = new BookDto(
            bookId, $"Book {bookId:N}", "9780134685991", 2020, "en", null, Guid.NewGuid(), "Category", null, null,
            ["Author"], TotalCopies: 1, AvailableCopies: 0, IsDeleted: isDeleted);
    }

    public Task<BookCopyDto?> GetCopyByBarcodeAsync(
        string barcode,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        LookupCalls++;

        BookCopyDto? copy = _copies.Values.FirstOrDefault(copy =>
            string.Equals(
                copy.Barcode.Trim(),
                barcode.Trim(),
                StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(copy);
    }

    public Task SetCopyStatusAsync(
        Guid copyId,
        BookCopyStatus status,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        SetStatusCalls++;

        if (ThrowOnNextSetCopyStatus)
        {
            ThrowOnNextSetCopyStatus = false;

            throw new InvalidOperationException(
                "Simulated Catalog failure.");
        }

        if (!_copies.TryGetValue(copyId, out var copy))
            throw new DomainException(Error.NotFound(
                "catalog.copy_not_found",
                "The copy was not found."));

        bool allowed = (copy.Status, status) switch
        {
            (BookCopyStatus.Available, BookCopyStatus.OnLoan or BookCopyStatus.Reserved) => true,
            (BookCopyStatus.Reserved, BookCopyStatus.OnLoan or BookCopyStatus.Available) => true,
            (BookCopyStatus.OnLoan, BookCopyStatus.Lost) => true,
            _ => false,
        };
        if (!allowed)
        {
            throw new DomainException(
                CatalogErrors.CopyStatusChangeNotAllowed(
                    copy.Status, status));
        }

        _copies[copyId] = copy with { Status = status };
        return Task.CompletedTask;
    }

    public Dictionary<Guid, BookCopyDto> Snapshot() => new(_copies);

    public void Restore(Dictionary<Guid, BookCopyDto> snapshot)
        => _copies = new(snapshot);

    public Task<BookCopyDto?> GetAvailableCopyAsync(
        Guid bookId, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<BookDto?> GetBookAsync(
        Guid bookId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (_books is null)
            throw new NotSupportedException("No book was seeded for this test.");

        return Task.FromResult(_books.GetValueOrDefault(bookId));
    }

    public Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(
        IReadOnlyCollection<Guid> copyIds,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<BookCopyDto> copies = _copies.Values
            .Where(copy => copyIds.Contains(copy.Id))
            .ToList();

        return Task.FromResult(copies);
    }
    public Task<IReadOnlyList<Guid>> GetCopyIdsAsync(
        Guid bookId, CancellationToken ct = default)
        => throw new NotSupportedException();
}
