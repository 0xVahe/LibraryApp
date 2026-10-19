using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Catalog;

public class AuthorServiceTests
{
    private readonly FakeAuthors _repository = new();
    private readonly TestTransaction _transaction = new();
    private AuthorService Service => new(_repository, _transaction, _transaction, _transaction);

    [Fact]
    public async Task Create_update_read_and_delete_unreferenced_author()
    {
        var author = await Service.CreateAsync(new AuthorRequest("  Demo Author  "));
        Assert.Equal("Demo Author", author.FullName);
        await Service.UpdateAsync(author.Id, new AuthorRequest("Updated Author"));
        Assert.Equal("Updated Author", (await Service.GetAsync(author.Id)).FullName);
        Assert.Single(await Service.ListAsync());
        await Service.DeleteAsync(author.Id);
        Assert.Empty(_repository.Authors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_or_null_author_list_cannot_remove_last_author(bool useNull)
    {
        var author = await Service.CreateAsync(new AuthorRequest("Demo Author"));
        _repository.Links.Add(new BookAuthor(_repository.BookId, author.Id));
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            Service.SetBookAuthorsAsync(_repository.BookId, new BookAuthorsRequest(useNull ? null : [])));
        Assert.Equal(CatalogErrors.BookAuthorsRequired, error.Error);
        Assert.Single(_repository.Links);
    }

    [Fact]
    public async Task Replacement_preserves_retained_links_and_deduplicates_requested_authors()
    {
        var first = await Service.CreateAsync(new AuthorRequest("First"));
        var second = await Service.CreateAsync(new AuthorRequest("Second"));
        var third = await Service.CreateAsync(new AuthorRequest("Third"));
        await Service.SetBookAuthorsAsync(_repository.BookId, new BookAuthorsRequest([first.Id, second.Id]));
        var retained = _repository.Links.Single(x => x.AuthorId == second.Id);

        await Service.SetBookAuthorsAsync(_repository.BookId, new BookAuthorsRequest([second.Id, third.Id, third.Id]));

        Assert.Equal(2, _repository.Links.Count);
        Assert.Contains(retained, _repository.Links);
        Assert.DoesNotContain(_repository.Links, x => x.AuthorId == first.Id);
        Assert.Contains(_repository.Links, x => x.AuthorId == third.Id);
        Assert.Equal(2, _transaction.Locks.Count);
    }

    [Fact]
    public async Task Missing_author_does_not_modify_existing_links()
    {
        var author = await Service.CreateAsync(new AuthorRequest("Demo Author"));
        _repository.Links.Add(new BookAuthor(_repository.BookId, author.Id));
        var error = await Assert.ThrowsAsync<DomainException>(() => Service.SetBookAuthorsAsync(
            _repository.BookId, new BookAuthorsRequest([Guid.NewGuid()])));
        Assert.Equal(CatalogErrors.AuthorNotFound, error.Error);
        Assert.Equal(author.Id, Assert.Single(_repository.Links).AuthorId);
    }

    [Fact]
    public async Task Missing_book_is_rejected()
    {
        var author = await Service.CreateAsync(new AuthorRequest("Demo Author"));
        var error = await Assert.ThrowsAsync<DomainException>(() => Service.SetBookAuthorsAsync(
            Guid.NewGuid(), new BookAuthorsRequest([author.Id])));
        Assert.Equal(CatalogErrors.BookNotFound, error.Error);
        Assert.Empty(_repository.Links);
    }

    [Fact]
    public async Task Linked_author_cannot_be_deleted()
    {
        var author = await Service.CreateAsync(new AuthorRequest("Demo Author"));
        _repository.Links.Add(new BookAuthor(_repository.BookId, author.Id));
        var error = await Assert.ThrowsAsync<DomainException>(() => Service.DeleteAsync(author.Id));
        Assert.Equal(CatalogErrors.AuthorInUse, error.Error);
        Assert.Single(_repository.Authors);
        Assert.Single(_repository.Links);
    }

    [Fact]
    public async Task Missing_author_returns_not_found()
    {
        var error = await Assert.ThrowsAsync<DomainException>(() => Service.GetAsync(Guid.NewGuid()));
        Assert.Equal(CatalogErrors.AuthorNotFound, error.Error);
    }

    [Fact]
    public async Task Book_author_ids_include_all_linked_authors_even_when_names_match()
    {
        var first = await Service.CreateAsync(new AuthorRequest("Same Name"));
        var second = await Service.CreateAsync(new AuthorRequest("Same Name"));
        _repository.Links.Add(new BookAuthor(_repository.BookId, first.Id));
        _repository.Links.Add(new BookAuthor(_repository.BookId, second.Id));
        var repository = new BookAuthorsRepository(_repository);
        IAuthorService service = new AuthorService(repository, _transaction, _transaction, _transaction);
        using var cancellation = new CancellationTokenSource();

        var ids = await service.GetBookAuthorIdsAsync(_repository.BookId, cancellation.Token);

        Assert.Equal(new[] { first.Id, second.Id }.OrderBy(x => x), ids.OrderBy(x => x));
        Assert.Equal((_repository.BookId, cancellation.Token), Assert.Single(repository.BookExistsCalls));
        Assert.Equal((_repository.BookId, cancellation.Token), Assert.Single(repository.BookAuthorsCalls));
    }

    [Fact]
    public async Task Unknown_or_deleted_book_returns_not_found_without_reading_authors()
    {
        var bookId = Guid.NewGuid();
        var repository = new BookAuthorsRepository(_repository);
        var service = new AuthorService(repository, _transaction, _transaction, _transaction);
        using var cancellation = new CancellationTokenSource();

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.GetBookAuthorIdsAsync(bookId, cancellation.Token));

        Assert.Equal(CatalogErrors.BookNotFound, error.Error);
        Assert.Equal((bookId, cancellation.Token), Assert.Single(repository.BookExistsCalls));
        Assert.Empty(repository.BookAuthorsCalls);
    }

    [Fact]
    public async Task Book_without_linked_authors_returns_empty_ids()
    {
        var repository = new BookAuthorsRepository(_repository);
        var service = new AuthorService(repository, _transaction, _transaction, _transaction);
        using var cancellation = new CancellationTokenSource();

        var ids = await service.GetBookAuthorIdsAsync(_repository.BookId, cancellation.Token);

        Assert.Empty(ids);
        Assert.Equal((_repository.BookId, cancellation.Token), Assert.Single(repository.BookExistsCalls));
        Assert.Equal((_repository.BookId, cancellation.Token), Assert.Single(repository.BookAuthorsCalls));
    }

    private sealed class BookAuthorsRepository(FakeAuthors repository) : IAuthorRepository
    {
        public List<(Guid BookId, CancellationToken Token)> BookExistsCalls { get; } = [];
        public List<(Guid BookId, CancellationToken Token)> BookAuthorsCalls { get; } = [];

        public Task<bool> BookExistsAsync(Guid bookId, CancellationToken ct)
        {
            BookExistsCalls.Add((bookId, ct));
            return repository.BookExistsAsync(bookId, ct);
        }

        public Task<IReadOnlyList<BookAuthor>> GetBookAuthorsAsync(Guid bookId, CancellationToken ct)
        {
            BookAuthorsCalls.Add((bookId, ct));
            return repository.GetBookAuthorsAsync(bookId, ct);
        }

        public Task<IReadOnlyList<AuthorDto>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<Author?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public void Add(Author author) => throw new NotSupportedException();
        public void Remove(Author author) => throw new NotSupportedException();
        public Task<bool> IsInUseAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountAuthorsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        public void AddLink(BookAuthor link) => throw new NotSupportedException();
        public void RemoveLink(BookAuthor link) => throw new NotSupportedException();
    }
}
