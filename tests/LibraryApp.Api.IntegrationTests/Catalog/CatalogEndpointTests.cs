using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

public class CatalogEndpointTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                factory.Account(role)).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.Librarian, HttpStatusCode.Created)]
    [InlineData(StaffRoles.LibraryAdministrator, HttpStatusCode.Created)]
    public async Task Author_and_category_writes_require_librarian_or_administrator(string? role, HttpStatusCode expected)
    {
        using var client = Client(role);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/authors", new AuthorRequest("Synthetic Author"))).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/categories", new CategoryRequest("Synthetic Category", null))).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.Librarian, HttpStatusCode.NoContent)]
    [InlineData(StaffRoles.LibraryAdministrator, HttpStatusCode.NoContent)]
    public async Task Author_and_category_updates_and_deletes_enforce_roles(string? role, HttpStatusCode expected)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var author = new Author(Guid.NewGuid(), "Original author");
        var category = new Category(Guid.NewGuid(), "Original category");
        db.AddRange(author, category);
        await db.SaveChangesAsync();

        using var client = Client(role);
        Assert.Equal(expected, (await client.PutAsJsonAsync($"/api/authors/{author.Id}", new AuthorRequest("Updated author"))).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync($"/api/categories/{category.Id}", new CategoryRequest("Updated category", null))).StatusCode);

        db.ChangeTracker.Clear();
        var allowed = expected == HttpStatusCode.NoContent;
        Assert.Equal(allowed ? "Updated author" : "Original author", (await db.Authors.SingleAsync(x => x.Id == author.Id)).FullName);
        Assert.Equal(allowed ? "Updated category" : "Original category", (await db.Categories.SingleAsync(x => x.Id == category.Id)).Name);

        Assert.Equal(expected, (await client.DeleteAsync($"/api/authors/{author.Id}")).StatusCode);
        Assert.Equal(expected, (await client.DeleteAsync($"/api/categories/{category.Id}")).StatusCode);
        Assert.Equal(!allowed, await db.Authors.AnyAsync(x => x.Id == author.Id));
        Assert.Equal(!allowed, await db.Categories.AnyAsync(x => x.Id == category.Id));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized, "9780134685991")]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden, "9780132350884")]
    [InlineData(StaffRoles.Librarian, HttpStatusCode.NoContent, "9780201633610")]
    [InlineData(StaffRoles.LibraryAdministrator, HttpStatusCode.NoContent, "9780134494166")]
    public async Task Valid_book_author_replacement_enforces_roles(string? role, HttpStatusCode expected, string isbn)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var original = new Author(Guid.NewGuid(), "Original author");
        var replacement = new Author(Guid.NewGuid(), "Replacement author");
        var category = new Category(Guid.NewGuid(), "Book category");
        var book = new Book(Guid.NewGuid(), "Synthetic book", isbn, 2020, "en", null, category.Id, null);
        db.AddRange(original, replacement, category, book, new BookAuthor(book.Id, original.Id));
        await db.SaveChangesAsync();

        using var client = Client(role);
        var response = await client.PutAsJsonAsync($"/api/books/{book.Id}/authors", new BookAuthorsRequest([replacement.Id]));
        Assert.Equal(expected, response.StatusCode);

        db.ChangeTracker.Clear();
        var link = await db.BookAuthors.SingleAsync(x => x.BookId == book.Id);
        Assert.Equal(expected == HttpStatusCode.NoContent ? replacement.Id : original.Id, link.AuthorId);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.Librarian, HttpStatusCode.BadRequest)]
    public async Task Book_author_linking_is_authorized_and_validated(string? role, HttpStatusCode expected)
    {
        using var client = Client(role);
        var response = await client.PutAsJsonAsync($"/api/books/{Guid.NewGuid()}/authors", new BookAuthorsRequest([]));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Category_cycle_returns_422_problem_details_and_keeps_existing_parent()
    {
        using var client = Client(StaffRoles.Librarian);
        var response = await client.PostAsJsonAsync("/api/categories", new CategoryRequest("Root", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var root = (await response.Content.ReadFromJsonAsync<CategoryDto>())!;
        var childResponse = await client.PostAsJsonAsync("/api/categories", new CategoryRequest("Child", root.Id));
        Assert.Equal(HttpStatusCode.Created, childResponse.StatusCode);
        var child = (await childResponse.Content.ReadFromJsonAsync<CategoryDto>())!;

        var invalid = await client.PutAsJsonAsync($"/api/categories/{root.Id}", new CategoryRequest("Root", child.Id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("catalog.category_parent_not_top_level", problem.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
        Assert.Null((await client.GetFromJsonAsync<CategoryDto>($"/api/categories/{root.Id}"))!.ParentCategoryId);
    }

    [Fact]
    public async Task Invalid_name_returns_validation_problem_and_missing_author_returns_404()
    {
        using var client = Client(StaffRoles.Librarian);
        var invalid = await client.PostAsJsonAsync("/api/authors", new AuthorRequest(" "));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/authors/{Guid.NewGuid()}")).StatusCode);
    }
}
