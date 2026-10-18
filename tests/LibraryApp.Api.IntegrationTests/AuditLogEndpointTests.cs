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

/// <summary>NFR-14 through the real host: a change made by a signed-in librarian is recorded with the user of the token.</summary>
public class AuditLogEndpointTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    [Fact]
    public async Task Creating_a_book_records_the_user_of_the_access_token()
    {
        var userId = Guid.NewGuid();
        var category = new Category(Guid.NewGuid(), $"Audit category {Guid.NewGuid():N}");
        var author = new Author(Guid.NewGuid(), "Audit author");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            db.AddRange(category, author);
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

        using var verify = factory.Services.CreateScope();
        string bookId = book.Id.ToString();
        AuditLog row = await verify.ServiceProvider.GetRequiredService<LibraryDbContext>().AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityName == nameof(Book) && a.EntityId == bookId);
        Assert.Equal((AuditActions.Created, (Guid?)userId), (row.Action, row.UserId));
    }
}
