using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>
/// Temporary in-memory stand-in for <see cref="ICatalogService"/>, so Borrowing can code and test against the frozen
/// contract before the real Catalog service exists ("Until a provider is ready, consumers code against a stub").
/// Group 1 replaces it with the real service in a separate pull request after that service is merged.
/// <para>
/// Not registered in dependency injection: create it in tests, or register it yourself until the Catalog module
/// registration exists. Each instance has its own data (fixed ids below). Not thread-safe.
/// </para>
/// <para>
/// Status changes use the real <see cref="BookCopy"/> entity, so the copy status transition rules are the same as in
/// the real service.
/// </para>
/// </summary>
public sealed class CatalogServiceStub : ICatalogService
{
    // ---------- Books ----------

    /// <summary>Has copies BK-0001-01 and BK-0001-02 (Available), BK-0001-03 (OnLoan), BK-0001-04 (Lost).</summary>
    public static readonly Guid AvailableBookId = new("b0000000-0000-0000-0000-000000000001");

    /// <summary>Has one copy BK-0002-01 (OnLoan), so no copy is Available: a book that can be reserved.</summary>
    public static readonly Guid UnavailableBookId = new("b0000000-0000-0000-0000-000000000002");

    /// <summary>A soft-deleted book with one copy BK-0003-01 (Withdrawn).</summary>
    public static readonly Guid DeletedBookId = new("b0000000-0000-0000-0000-000000000003");

    // ---------- Copies ----------

    /// <summary>BK-0001-01, Available.</summary>
    public static readonly Guid AvailableCopyId = new("c0000000-0000-0000-0000-000000000001");

    /// <summary>BK-0001-02, Available.</summary>
    public static readonly Guid SecondAvailableCopyId = new("c0000000-0000-0000-0000-000000000002");

    /// <summary>BK-0001-03, OnLoan.</summary>
    public static readonly Guid OnLoanCopyId = new("c0000000-0000-0000-0000-000000000003");

    /// <summary>BK-0001-04, Lost.</summary>
    public static readonly Guid LostCopyId = new("c0000000-0000-0000-0000-000000000004");

    /// <summary>BK-0002-01, OnLoan.</summary>
    public static readonly Guid UnavailableBookCopyId = new("c0000000-0000-0000-0000-000000000005");

    /// <summary>BK-0003-01, Withdrawn.</summary>
    public static readonly Guid DeletedBookCopyId = new("c0000000-0000-0000-0000-000000000006");

    // The real service takes this error from CatalogErrors; it is added there together with the real service.
    private static readonly Error CopyNotFound =
        Error.NotFound("catalog.copy_not_found", "The copy was not found.");

    private static readonly Guid RiskCategoryId = new("ca000000-0000-0000-0000-000000000001");
    private static readonly Guid ItCategoryId = new("ca000000-0000-0000-0000-000000000002");

    private readonly List<StubBook> _books;
    private readonly List<BookCopy> _copies;

    public CatalogServiceStub()
    {
        _books = new List<StubBook>
        {
            new StubBook(AvailableBookId, "Risk Management Basics", "9780134685991", 2020, "English",
                "Introduction to banking risk.", RiskCategoryId, "Risk, Compliance & AML",
                null, null, new[] { "Anna Petrosyan" }, IsDeleted: false),

            new StubBook(UnavailableBookId, "Clean Code in C#", "9781838982973", 2021, "English",
                null, ItCategoryId, "IT & Software Engineering",
                null, null, new[] { "Aram Hakobyan", "Lilit Sargsyan" }, IsDeleted: false),

            new StubBook(DeletedBookId, "Old Payment Systems", "9780306406157", 2005, "Armenian",
                null, RiskCategoryId, "Risk, Compliance & AML",
                null, null, new[] { "Gor Grigoryan" }, IsDeleted: true),
        };

        _copies = new List<BookCopy>
        {
            CreateCopy(AvailableCopyId, AvailableBookId, "BK-0001-01", BookCopyStatus.Available),
            CreateCopy(SecondAvailableCopyId, AvailableBookId, "BK-0001-02", BookCopyStatus.Available),
            CreateCopy(OnLoanCopyId, AvailableBookId, "BK-0001-03", BookCopyStatus.OnLoan),
            CreateCopy(LostCopyId, AvailableBookId, "BK-0001-04", BookCopyStatus.Lost),
            CreateCopy(UnavailableBookCopyId, UnavailableBookId, "BK-0002-01", BookCopyStatus.OnLoan),
            CreateCopy(DeletedBookCopyId, DeletedBookId, "BK-0003-01", BookCopyStatus.Withdrawn),
        };
    }

    public Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        StubBook? book = _books.FirstOrDefault(b => b.Id == bookId);
        if (book is null || book.IsDeleted)
        {
            return Task.FromResult<BookCopyDto?>(null);
        }

        BookCopy? copy = _copies
            .Where(c => c.BookId == bookId && c.Status == BookCopyStatus.Available)
            .OrderBy(c => c.Barcode, StringComparer.Ordinal)
            .FirstOrDefault();

        BookCopyDto? result = copy is null ? null : ToDto(copy);
        return Task.FromResult(result);
    }

    public Task SetCopyStatusAsync(Guid copyId, BookCopyStatus status, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        BookCopy? copy = _copies.FirstOrDefault(c => c.Id == copyId);
        if (copy is null)
        {
            throw new DomainException(CopyNotFound);
        }

        // Throws catalog.copy_status_change_not_allowed when the transition is not allowed.
        copy.ChangeStatus(status);
        return Task.CompletedTask;
    }

    public Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        StubBook? book = _books.FirstOrDefault(b => b.Id == bookId);
        BookDto? result = book is null ? null : ToDto(book);
        return Task.FromResult(result);
    }

    public Task<BookCopyDto?> GetCopyByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(barcode))
        {
            return Task.FromResult<BookCopyDto?>(null);
        }

        // Barcodes are stored trimmed and in upper case, so a scanned "bk-0001-01 " still matches.
        string normalizedBarcode = barcode.Trim().ToUpperInvariant();

        BookCopy? copy = _copies.FirstOrDefault(c => c.Barcode == normalizedBarcode);
        BookCopyDto? result = copy is null ? null : ToDto(copy);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        HashSet<Guid> wantedIds = copyIds.ToHashSet();

        List<BookCopyDto> result = _copies
            .Where(c => wantedIds.Contains(c.Id))
            .Select(ToDto)
            .ToList();

        return Task.FromResult<IReadOnlyList<BookCopyDto>>(result);
    }

    public Task<IReadOnlyList<Guid>> GetCopyIdsAsync(Guid bookId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        List<Guid> result = _copies
            .Where(c => c.BookId == bookId)
            .Select(c => c.Id)
            .ToList();

        return Task.FromResult<IReadOnlyList<Guid>>(result);
    }

    private static BookCopy CreateCopy(Guid id, Guid bookId, string barcode, BookCopyStatus status)
    {
        // A new copy starts as Available; the other statuses are reached through allowed transitions.
        var copy = new BookCopy(id, bookId, barcode, BookCopyCondition.Good, "A-1");

        if (status != BookCopyStatus.Available)
        {
            copy.ChangeStatus(status);
        }

        return copy;
    }

    private static BookCopyDto ToDto(BookCopy copy)
    {
        return new BookCopyDto(copy.Id, copy.BookId, copy.Barcode, copy.Status, copy.Condition, copy.ShelfLocation);
    }

    private BookDto ToDto(StubBook book)
    {
        List<BookCopy> copiesOfBook = _copies.Where(c => c.BookId == book.Id).ToList();

        // Lost and Withdrawn copies are no longer in the library, so they are not counted (see BookDto.TotalCopies).
        int totalCopies = copiesOfBook.Count(c =>
            c.Status != BookCopyStatus.Lost && c.Status != BookCopyStatus.Withdrawn);

        int availableCopies = copiesOfBook.Count(c => c.Status == BookCopyStatus.Available);

        return new BookDto(
            book.Id,
            book.Title,
            book.Isbn,
            book.PublicationYear,
            book.Language,
            book.Description,
            book.CategoryId,
            book.CategoryName,
            book.PublisherId,
            book.PublisherName,
            book.Authors,
            totalCopies,
            availableCopies,
            book.IsDeleted);
    }

    private sealed record StubBook(
        Guid Id,
        string Title,
        string Isbn,
        int PublicationYear,
        string Language,
        string? Description,
        Guid CategoryId,
        string CategoryName,
        Guid? PublisherId,
        string? PublisherName,
        IReadOnlyList<string> Authors,
        bool IsDeleted);
}
