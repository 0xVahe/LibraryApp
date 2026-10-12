using System.Net;
using System.Net.Http.Json;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

public class PublisherDatabaseEndpointTests(ApiHostFactory factory)
    : PublisherEndpointChecks<ApiHostFactory>(factory), IClassFixture<ApiHostFactory>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Books_including_soft_deleted_books_block_deletion_with_409(bool deleted)
    {
        using var client = Client(StaffRoles.Librarian);
        var publisher = await Create(client, "Used Publisher");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var category = new Category(Guid.NewGuid(), "Synthetic category");
        var book = new Book(Guid.NewGuid(), "Synthetic book", deleted ? "9780132350884" : "9780134685991",
            2020, "en", null, category.Id, publisher.Id);
        if (deleted) book.Delete(DateTime.UtcNow);
        db.AddRange(category, book);
        await db.SaveChangesAsync();

        using var response = await client.DeleteAsync($"/api/publishers/{publisher.Id}");
        await AssertProblem(response, HttpStatusCode.Conflict, "catalog.publisher_in_use");
        Assert.Equal(publisher, await client.GetFromJsonAsync<PublisherDto>($"/api/publishers/{publisher.Id}"));
        db.ChangeTracker.Clear();
        Assert.Equal(deleted, (await db.Books.IgnoreQueryFilters().SingleAsync(x => x.Id == book.Id)).IsDeleted);
        Assert.Equal(publisher.Id, (await db.Books.IgnoreQueryFilters().SingleAsync(x => x.Id == book.Id)).PublisherId);
    }
}
