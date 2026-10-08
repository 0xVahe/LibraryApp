using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Tests.Catalog;

internal sealed class TestTransaction : IUnitOfWork, IApplicationLock, IIdGenerator
{
    public bool Active { get; private set; }
    public List<string> Locks { get; } = [];
    public Guid NewId() => Guid.NewGuid();

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        Assert.False(Active);
        Active = true;
        try { return await operation(ct); }
        finally { Active = false; }
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
        ExecuteInTransactionAsync(async token => { await operation(token); return true; }, ct);

    public Task AcquireAsync(string resource, CancellationToken ct = default)
    {
        Assert.True(Active);
        Locks.Add(resource);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAuthors : IAuthorRepository
{
    public List<Author> Authors { get; } = [];
    public List<BookAuthor> Links { get; } = [];
    public Guid BookId { get; } = Guid.NewGuid();
    public Task<IReadOnlyList<AuthorDto>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AuthorDto>>(Authors.Select(x => new AuthorDto(x.Id, x.FullName)).ToArray());
    public Task<Author?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Authors.SingleOrDefault(x => x.Id == id));
    public void Add(Author author) => Authors.Add(author);
    public void Remove(Author author) => Authors.Remove(author);
    public Task<bool> IsInUseAsync(Guid id, CancellationToken ct) => Task.FromResult(Links.Any(x => x.AuthorId == id));
    public Task<bool> BookExistsAsync(Guid bookId, CancellationToken ct) => Task.FromResult(bookId == BookId);
    public Task<int> CountAuthorsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult(Authors.Count(x => ids.Contains(x.Id)));
    public Task<IReadOnlyList<BookAuthor>> GetBookAuthorsAsync(Guid bookId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BookAuthor>>(Links.Where(x => x.BookId == bookId).ToArray());
    public void AddLink(BookAuthor link) => Links.Add(link);
    public void RemoveLink(BookAuthor link) => Links.Remove(link);
}

internal sealed class FakeCategories : ICategoryRepository
{
    public List<Category> Categories { get; } = [];
    public HashSet<Guid> ReferencedIds { get; } = [];
    public Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CategoryDto>>(Categories.Select(x => new CategoryDto(x.Id, x.Name, x.ParentCategoryId)).ToArray());
    public Task<Category?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Categories.SingleOrDefault(x => x.Id == id));
    public Task<bool> HasBooksAsync(Guid id, CancellationToken ct) => Task.FromResult(ReferencedIds.Contains(id));
    public void Add(Category category) => Categories.Add(category);
    public void Remove(Category category) => Categories.Remove(category);
}
