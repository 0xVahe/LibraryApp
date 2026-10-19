using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Catalog;

/// <summary>CAT-09: several books in one transaction, with the rules of a single book.</summary>
public class BookServiceBatchTests
{
    private readonly FakeBooks _books = new();
    private readonly FakeAuthors _authors = new();
    private readonly TestTransaction _transaction = new();
    private readonly Guid _categoryId = Guid.NewGuid();
    private readonly Author _author = new(Guid.NewGuid(), "Batch Author");

    public BookServiceBatchTests()
    {
        _books.CategoryIds.Add(_categoryId);
        _authors.Authors.Add(_author);
    }

    private BookService Service => new(_books, _authors, _transaction, _transaction, _transaction,
        new TestClock(new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc)));

    private CreateBookRequest Request(string isbn) =>
        new("Batch Book", isbn, 2020, "en", null, _categoryId, null, [_author.Id]);

    [Fact]
    public async Task A_batch_creates_every_book_in_one_transaction_and_returns_the_ids_in_order()
    {
        var ids = await Service.CreateBatchAsync([Request("9791000000015"), Request("9791000000022")]);

        Assert.Equal(2, ids.Count);
        Assert.Equal(ids, _books.Books.Select(x => x.Id));
        Assert.Equal(2, _authors.Links.Count);
        Assert.Equal(new[] { "catalog:books" }, _transaction.Locks);   // one lock for the whole batch
        Assert.False(_transaction.Active);
    }

    [Fact]
    public async Task The_same_ISBN_twice_in_a_batch_is_refused()
    {
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            Service.CreateBatchAsync([Request("9791000000015"), Request("979-1-00000-001-5")]));

        Assert.Equal(CatalogErrors.BookIsbnDuplicate, error.Error);
    }

    [Fact]
    public async Task A_broken_rule_in_any_book_stops_the_batch()
    {
        var unknownCategory = Request("9791000000022") with { CategoryId = Guid.NewGuid() };

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            Service.CreateBatchAsync([Request("9791000000015"), unknownCategory]));

        Assert.Equal(CatalogErrors.CategoryNotFound, error.Error);
    }
}
