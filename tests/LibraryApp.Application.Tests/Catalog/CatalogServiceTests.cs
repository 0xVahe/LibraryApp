using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Catalog;

/// <summary>The Catalog contract for other modules (CAT-07, INT-01, INT-02). The SQL side is in CatalogServiceDatabaseTests.</summary>
public sealed class CatalogServiceTests
{
    private readonly FakeBookCopyRepository _repository = new();
    private readonly RecordingCopyQuery _query = new();
    private readonly StubDetailsQuery _details = new();
    private readonly CatalogService _service;

    public CatalogServiceTests()
    {
        _service = new CatalogService(_query, _repository, _details);
    }

    // ---------- SetCopyStatusAsync (INT-02) ----------

    [Fact]
    public async Task A_status_change_is_applied_to_the_copy()
    {
        BookCopy copy = _repository.AddCopy(_repository.AddBook(), "BK-0001-01");

        await _service.SetCopyStatusAsync(copy.Id, BookCopyStatus.OnLoan);

        Assert.Equal(BookCopyStatus.OnLoan, copy.Status);
    }

    [Fact]
    public async Task A_status_change_of_an_unknown_copy_is_refused()
    {
        var error = await Assert.ThrowsAsync<DomainException>(
            () => _service.SetCopyStatusAsync(Guid.NewGuid(), BookCopyStatus.OnLoan));

        Assert.Equal(CatalogErrors.CopyNotFound, error.Error);
    }

    [Fact]
    public async Task An_illegal_status_change_is_refused_and_the_copy_keeps_its_status()
    {
        BookCopy copy = _repository.AddCopy(_repository.AddBook(), "BK-0001-01", BookCopyStatus.Lost);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => _service.SetCopyStatusAsync(copy.Id, BookCopyStatus.Available));

        Assert.Equal(CatalogErrors.CopyStatusChangeNotAllowed(BookCopyStatus.Lost, BookCopyStatus.Available), error.Error);
        Assert.Equal(BookCopyStatus.Lost, copy.Status);
    }

    // ---------- GetCopyByBarcodeAsync ----------

    [Fact]
    public async Task A_barcode_is_trimmed_and_upper_cased_before_the_lookup()
    {
        _query.Copy = Dto("BK-0001-01");

        var copy = await _service.GetCopyByBarcodeAsync("  bk-0001-01 ");

        Assert.Equal("BK-0001-01", _query.Barcode);
        Assert.Same(_query.Copy, copy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task An_empty_barcode_finds_nothing_without_a_query(string? barcode)
    {
        var copy = await _service.GetCopyByBarcodeAsync(barcode!);

        Assert.Null(copy);
        Assert.Empty(_query.Calls);
    }

    // ---------- GetCopiesAsync ----------

    [Fact]
    public async Task No_ids_give_an_empty_list_without_a_query()
    {
        var copies = await _service.GetCopiesAsync([]);

        Assert.Empty(copies);
        Assert.Empty(_query.Calls);
    }

    [Fact]
    public async Task Repeated_ids_are_asked_for_once()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        await _service.GetCopiesAsync([first, second, first]);

        Assert.Equal(new[] { first, second }, _query.RequestedIds);
    }

    // ---------- lookups by book ----------

    [Fact]
    public async Task The_available_copy_and_the_copy_ids_are_read_for_the_given_book()
    {
        Guid bookId = Guid.NewGuid();
        _query.Copy = Dto("BK-0001-01");
        _query.CopyIds = [Guid.NewGuid(), Guid.NewGuid()];

        var available = await _service.GetAvailableCopyAsync(bookId);
        var ids = await _service.GetCopyIdsAsync(bookId);

        Assert.Same(_query.Copy, available);
        Assert.Equal(_query.CopyIds, ids);
        Assert.Equal(bookId, _query.BookId);
        Assert.Equal(new[] { "available", "ids" }, _query.Calls);
    }

    [Fact]
    public async Task A_deleted_book_is_still_returned_so_old_loans_keep_resolving()
    {
        _details.Book = new BookDto(
            Guid.NewGuid(), "Old Edition", "9781000000207", 2003, "en", null,
            Guid.NewGuid(), "IT & Software Engineering", null, null,
            ["Andrew Hunt"], TotalCopies: 0, AvailableCopies: 0, IsDeleted: true);

        var book = await _service.GetBookAsync(_details.Book.Id);

        Assert.NotNull(book);
        Assert.True(book.IsDeleted);
    }

    [Fact]
    public async Task An_unknown_book_is_null()
    {
        var book = await _service.GetBookAsync(Guid.NewGuid());

        Assert.Null(book);
    }

    private static BookCopyDto Dto(string barcode) =>
        new(Guid.NewGuid(), Guid.NewGuid(), barcode, BookCopyStatus.Available, BookCopyCondition.Good, "A-01");

    /// <summary>Returns what the test sets and remembers what the service asked for.</summary>
    private sealed class RecordingCopyQuery : ICatalogCopyQuery
    {
        public BookCopyDto? Copy { get; set; }
        public IReadOnlyList<Guid> CopyIds { get; set; } = [];
        public List<string> Calls { get; } = [];
        public string? Barcode { get; private set; }
        public Guid? BookId { get; private set; }
        public IReadOnlyCollection<Guid>? RequestedIds { get; private set; }

        public Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct)
        {
            Calls.Add("available");
            BookId = bookId;
            return Task.FromResult(Copy);
        }

        public Task<BookCopyDto?> GetByBarcodeAsync(string barcode, CancellationToken ct)
        {
            Calls.Add("barcode");
            Barcode = barcode;
            return Task.FromResult(Copy);
        }

        public Task<IReadOnlyList<BookCopyDto>> GetByIdsAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct)
        {
            Calls.Add("copies");
            RequestedIds = copyIds;
            return Task.FromResult<IReadOnlyList<BookCopyDto>>([]);
        }

        public Task<IReadOnlyList<Guid>> GetIdsByBookAsync(Guid bookId, CancellationToken ct)
        {
            Calls.Add("ids");
            BookId = bookId;
            return Task.FromResult(CopyIds);
        }
    }

    /// <summary>Returns the book the test sets when the id matches, otherwise null.</summary>
    private sealed class StubDetailsQuery : IBookDetailsQuery
    {
        public BookDto? Book { get; set; }

        public Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Book?.Id == bookId ? Book : null);
    }
}
