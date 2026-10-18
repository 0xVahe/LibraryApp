using LibraryApp.Application.Catalog.Dto;
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
}
