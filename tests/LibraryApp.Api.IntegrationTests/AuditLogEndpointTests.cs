using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Auditing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests;

/// <summary>NFR-14 through the real host: the changes a signed-in librarian makes to a book are recorded with the user of the token.</summary>
public class AuditLogEndpointTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    [Fact]
    public async Task Creating_a_book_changing_its_authors_and_deleting_it_are_recorded_with_the_user_of_the_access_token()
    {
        var userId = Guid.NewGuid();
        var category = new Category(Guid.NewGuid(), $"Audit category {Guid.NewGuid():N}");
        var author = new Author(Guid.NewGuid(), "Audit author");
        var coAuthor = new Author(Guid.NewGuid(), "Audit co-author");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            db.AddRange(category, author, coAuthor);
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<IAccessTokenIssuer>()
            .Issue(new LoginAccount(userId, "librarian@ameriabank.am", Guid.NewGuid(), StaffMemberStatus.Active, [StaffRoles.Librarian]))
            .Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/books", new CreateBookRequest(
            "Audited Through The API", "9780306406157", 2021, "en", null, category.Id, null, [author.Id]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var book = (await response.Content.ReadFromJsonAsync<BookDetailsDto>())!;

        // CAT-02: only the author links change.
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/books/{book.Id}/authors", new BookAuthorsRequest([author.Id, coAuthor.Id]))).StatusCode);
        // CAT-01, BR-CAT-3: soft delete.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/books/{book.Id}")).StatusCode);

        using var verify = factory.Services.CreateScope();
        string bookId = book.Id.ToString();
        List<AuditLog> rows = await verify.ServiceProvider.GetRequiredService<LibraryDbContext>().AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == nameof(Book) && a.EntityId == bookId).OrderBy(a => a.TimestampUtc).ThenBy(a => a.Id).ToListAsync();
        Assert.Equal([AuditActions.Created, AuditActions.Updated, AuditActions.Deleted], rows.Select(r => r.Action));
        Assert.All(rows, row => Assert.Equal(userId, row.UserId));
    }
}
