using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Catalog;

public class PublisherServiceTests
{
    private readonly FakePublishers _repository = new();
    private readonly TestTransaction _transaction = new();
    private PublisherService Service => new(_repository, _transaction, _transaction, _transaction);

    [Fact]
    public async Task Create_update_read_and_delete_unreferenced_publisher()
    {
        var publisher = await Service.CreateAsync(new PublisherRequest("  Demo Publisher  "));
        Assert.NotEqual(Guid.Empty, publisher.Id);
        Assert.Equal("Demo Publisher", publisher.Name);
        await Service.UpdateAsync(publisher.Id, new PublisherRequest(" Updated Publisher "));
        Assert.Equal("Updated Publisher", (await Service.GetAsync(publisher.Id)).Name);
        Assert.Equal(publisher.Id, Assert.Single(await Service.ListAsync()).Id);
        await Service.DeleteAsync(publisher.Id);
        Assert.Empty(_repository.Publishers);
        Assert.Equal("catalog:publishers", Assert.Single(_transaction.Locks));
        Assert.False(_transaction.Active);
    }

    [Fact]
    public async Task Names_of_200_characters_and_duplicate_names_are_allowed()
    {
        var name = new string('a', Publisher.NameMaxLength);
        var first = await Service.CreateAsync(new PublisherRequest(name));
        var second = await Service.CreateAsync(new PublisherRequest(name));
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(name, first.Name);
        await Service.UpdateAsync(second.Id, new PublisherRequest(name));
        Assert.Equal(name, (await Service.GetAsync(second.Id)).Name);
        Assert.Equal(2, (await Service.ListAsync()).Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task Missing_or_blank_names_cannot_create_or_rename_publishers(string? name)
    {
        var error = await Assert.ThrowsAsync<DomainException>(() => Service.CreateAsync(new PublisherRequest(name)));
        Assert.Equal(CatalogErrors.PublisherNameRequired, error.Error);
        Assert.Empty(_repository.Publishers);

        var publisher = await Service.CreateAsync(new PublisherRequest("Original"));
        error = await Assert.ThrowsAsync<DomainException>(() => Service.UpdateAsync(publisher.Id, new PublisherRequest(name)));
        Assert.Equal(CatalogErrors.PublisherNameRequired, error.Error);
        Assert.Equal("Original", (await Service.GetAsync(publisher.Id)).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Referenced_publisher_cannot_be_deleted_even_when_book_is_soft_deleted(bool deleted)
    {
        var publisher = await Service.CreateAsync(new PublisherRequest("Used publisher"));
        var book = new Book(Guid.NewGuid(), "Synthetic book", "9780134685991", 2020, "en", null, Guid.NewGuid(), publisher.Id);
        if (deleted) book.Delete(DateTime.UtcNow);
        _repository.Books.Add(book);

        var error = await Assert.ThrowsAsync<DomainException>(() => Service.DeleteAsync(publisher.Id));
        Assert.Equal(CatalogErrors.PublisherInUse, error.Error);
        Assert.Equal(ErrorKind.Conflict, error.Error.Kind);
        Assert.Single(_repository.Publishers);
        Assert.Single(_repository.Books);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Missing_publisher_returns_not_found(string operation)
    {
        var id = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<DomainException>(() => operation switch
        {
            "get" => Service.GetAsync(id),
            "update" => Service.UpdateAsync(id, new PublisherRequest("Updated")),
            _ => Service.DeleteAsync(id),
        });
        Assert.Equal(CatalogErrors.PublisherNotFound, error.Error);
        Assert.Equal(ErrorKind.NotFound, error.Error.Kind);
        Assert.Empty(_repository.Publishers);
    }
}
