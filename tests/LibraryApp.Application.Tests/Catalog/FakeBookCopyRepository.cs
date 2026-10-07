using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Tests.Catalog;

/// <summary>In-memory books and copies for the BookCopyService tests.</summary>
internal sealed class FakeBookCopyRepository : IBookCopyRepository
{
    private readonly Dictionary<Guid, BookState> _books = new();
    private readonly List<BookCopy> _copies = new();

    public IReadOnlyList<BookCopy> Copies => _copies;

    public Guid AddBook(BookState state = BookState.Active)
    {
        var bookId = Guid.NewGuid();
        _books[bookId] = state;
        return bookId;
    }

    /// <summary>Adds a copy in the given status (every status can be reached from Available).</summary>
    public BookCopy AddCopy(Guid bookId, string barcode, BookCopyStatus status = BookCopyStatus.Available)
    {
        var copy = new BookCopy(Guid.NewGuid(), bookId, barcode, BookCopyCondition.Good, "A-01");
        if (status != BookCopyStatus.Available)
            copy.ChangeStatus(status);

        _copies.Add(copy);
        return copy;
    }

    public Task<BookState> GetBookStateAsync(Guid bookId, CancellationToken ct) =>
        Task.FromResult(_books.TryGetValue(bookId, out var state) ? state : BookState.NotFound);

    public Task<bool> BarcodeExistsAsync(string barcode, CancellationToken ct) =>
        Task.FromResult(_copies.Any(c => c.Barcode == barcode));

    public Task<IReadOnlyList<BookCopy>> GetByBookAsync(Guid bookId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BookCopy>>(_copies
            .Where(c => c.BookId == bookId)
            .OrderBy(c => c.Barcode, StringComparer.Ordinal)
            .ToList());

    public Task<BookCopy?> GetByIdAsync(Guid copyId, CancellationToken ct) =>
        Task.FromResult(_copies.SingleOrDefault(c => c.Id == copyId));

    public void Add(BookCopy copy) => _copies.Add(copy);
}
